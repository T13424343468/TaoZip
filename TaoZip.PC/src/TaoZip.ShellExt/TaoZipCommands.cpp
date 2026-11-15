// TaoZipCommands.cpp - IExplorerCommand implementations.
// Shell UI path (GetTitle/GetIcon/GetState) must stay cheap: no IO, no process spawn.
// Heavy work (process spawn) only after Invoke (per MS Learn shell-extension guidance).
#include "TaoZipCommands.h"
#include "resource.h"

#include <shlwapi.h>
#include <shellapi.h>
#include <string>
#include <vector>
#include <algorithm>

#pragma comment(lib, "shlwapi.lib")

namespace TaoZip
{
    const wchar_t* const kExtractButtonText = L"TaoZip \x89E3\x538B";   // TaoZip 解压
    const wchar_t* const kCompressButtonText = L"TaoZip \x538B\x7F29"; // TaoZip 压缩
    const wchar_t* const kIconReference = L"TaoZipShellext.dll,-1";     // icon resource id 1

    // Same extension list as C# ArchiveKinds.OpenableExtensions (single-layer forms).
    // Case-insensitive comparisons; compounds handled by suffix check below.
    static const wchar_t* const kOpenableExts[] = {
        L".zip", L".7z", L".rar", L".tar", L".gz", L".bz2", L".xz", L".tgz", L".tbz2", L".txz"
    };
    static const wchar_t* const kCompoundSuffixes[] = {
        L".tar.gz", L".tar.bz2", L".tar.xz"
    };

    bool IsArchivePath(const wchar_t* path)
    {
        if (!path || !*path) return false;
        std::wstring lower(path);
        std::transform(lower.begin(), lower.end(), lower.begin(), ::towlower);
        for (const auto* e : kCompoundSuffixes)
        {
            const size_t elen = wcslen(e);
            if (lower.size() >= elen && lower.compare(lower.size() - elen, elen, e) == 0) return true;
        }
        const wchar_t* dot = wcsrchr(lower.c_str(), L'.');
        if (!dot) return false;
        for (const auto* e : kOpenableExts)
        {
            if (wcscmp(dot, e) == 0) return true;
        }
        return false;
    }

    bool IsDirectoryLikePath(const wchar_t* path)
    {
        if (!path || !*path) return false;
        const wchar_t* lastSlash = std::wcsrchr(path, L'\\');
        const wchar_t* lastFwd = std::wcsrchr(path, L'/');
        const wchar_t* lastSep = (lastSlash && lastFwd) ? (lastSlash > lastFwd ? lastSlash : lastFwd)
                                                       : (lastSlash ? lastSlash : lastFwd);
        if (lastSep && *(lastSep + 1) == L'\0') return true;  // trailing separator: folder
        // Extension dot must live after the last separator to count as a file extension.
        const wchar_t* lastDot = std::wcsrchr(lastSep ? lastSep : path, L'.');
        return lastDot == nullptr;
    }

    int DecideButton(IShellItemArray* selection)
    {
        // Same semantics as C# ExplorerButtonPolicy.Decide.
        if (!selection) return 0;
        DWORD count = 0;
        if (FAILED(selection->GetCount(&count)) || count == 0) return 0;
        bool hasArchive = false;
        bool hasOther = false;
        for (DWORD i = 0; i < count; ++i)
        {
            IShellItem* item = nullptr;
            if (FAILED(selection->GetItemAt(i, &item)) || !item) continue;
            LPWSTR path = nullptr;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path)
            {
                if (IsDirectoryLikePath(path)) { hasOther = true; }
                else if (IsArchivePath(path)) { hasArchive = true; }
                else { hasOther = true; }
                CoTaskMemFree(path);
            }
            else
            {
                // Virtual item (no filesystem path): treat as compressible.
                hasOther = true;
            }
            item->Release();
        }
        if (hasArchive) return 1;
        if (hasOther) return 2;
        return 0;
    }

    // ---- IUnknown ----
    IFACEMETHODIMP CommandBase::QueryInterface(REFIID riid, void** ppv)
    {
        if (!ppv) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IExplorerCommand))
        {
            *ppv = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) CommandBase::AddRef()
    {
        return InterlockedIncrement(&_refCount);
    }

    IFACEMETHODIMP_(ULONG) CommandBase::Release()
    {
        ULONG c = InterlockedDecrement(&_refCount);
        if (c == 0) delete this;
        return c;
    }

    // ---- IExplorerCommand plumbing (cheap) ----
    IFACEMETHODIMP CommandBase::GetTitle(IShellItemArray*, LPWSTR* ppszName)
    {
        const wchar_t* text = (_buttonKind == 1) ? kExtractButtonText : kCompressButtonText;
        return SHStrDupW(text, ppszName);
    }

    IFACEMETHODIMP CommandBase::GetIcon(IShellItemArray*, LPWSTR* ppszIcon)
    {
        return SHStrDupW(kIconReference, ppszIcon);
    }

    IFACEMETHODIMP CommandBase::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip)
    {
        *ppszInfotip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP CommandBase::GetCanonicalName(GUID* pguidCommandName)
    {
        *pguidCommandName = GUID_NULL;
        return S_OK;
    }

    IFACEMETHODIMP CommandBase::GetState(IShellItemArray* psiItemArray, BOOL, EXPCMDSTATE* pCmdState)
    {
        const int decided = DecideButton(psiItemArray);
        if (decided == _buttonKind)
            *pCmdState = ECS_ENABLED;
        else
            *pCmdState = ECS_HIDDEN;
        return S_OK;
    }

    IFACEMETHODIMP CommandBase::GetFlags(EXPCMDFLAGS* pFlags)
    {
        *pFlags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP CommandBase::EnumSubCommands(IEnumExplorerCommand** ppEnum)
    {
        *ppEnum = nullptr;
        return E_NOTIMPL;
    }

    // ---- Invoke: spawn TaoZip.exe with the standard switch, one item per arg ----
    void CommandBase::LaunchTaoZip(IShellItemArray* selection, const wchar_t* switchName)
    {
        if (!selection) return;
        // Resolve installed TaoZip.exe from HKLM (per-machine install), fall back to D:\Program Files.
        wchar_t exePath[MAX_PATH] = L"D:\\Program Files\\TaoZip\\TaoZip.exe";
        HKEY hk = nullptr;
        if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"Software\\Tao\\TaoZip", 0, KEY_READ, &hk) == ERROR_SUCCESS)
        {
            wchar_t dir[MAX_PATH] = L"";
            DWORD len = sizeof(dir);
            if (RegGetValueW(hk, nullptr, L"InstallDir", RRF_RT_REG_SZ, nullptr, dir, &len) == ERROR_SUCCESS && dir[0])
            {
                swprintf_s(exePath, MAX_PATH, L"%s\\TaoZip.exe", dir);
            }
            RegCloseKey(hk);
        }

        // Collect selected paths.
        std::vector<std::wstring> paths;
        DWORD count = 0;
        if (SUCCEEDED(selection->GetCount(&count)))
        {
            for (DWORD i = 0; i < count; ++i)
            {
                IShellItem* item = nullptr;
                if (SUCCEEDED(selection->GetItemAt(i, &item)) && item)
                {
                    LPWSTR p = nullptr;
                    if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &p)) && p)
                    {
                        paths.emplace_back(p);
                        CoTaskMemFree(p);
                    }
                    item->Release();
                }
            }
        }
        if (paths.empty()) return;

        // Build quoted command line: "exe" /switch "path1" "path2" ...
        std::wstring cmdline = std::wstring(L"\"") + exePath + L"\" " + switchName;
        for (const auto& p : paths)
        {
            cmdline += L" \"";
            cmdline += p;
            cmdline += L"\"";
        }

        STARTUPINFOW si = {};
        si.cb = sizeof(si);
        PROCESS_INFORMATION pi = {};
        if (CreateProcessW(nullptr, cmdline.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &si, &pi))
        {
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
        }
    }

    IFACEMETHODIMP ExtractCommand::Invoke(IShellItemArray* psiItemArray, IBindCtx*)
    {
        LaunchTaoZip(psiItemArray, L"/extract-here");
        return S_OK;
    }

    IFACEMETHODIMP CompressCommand::Invoke(IShellItemArray* psiItemArray, IBindCtx*)
    {
        LaunchTaoZip(psiItemArray, L"/compress-here");
        return S_OK;
    }
}
