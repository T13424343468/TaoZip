// TaoZipCommands.h - IExplorerCommand implementations (zero Chinese in identifiers).
// Visible strings are zh-CN literals; see const declarations below.
#pragma once

#include <windows.h>
#include <shobjidl.h>
#include <string>

namespace TaoZip
{
    // ---- Naming iron law: identifiers zero Chinese; user-visible text zh-CN ----
    extern const wchar_t* const kExtractButtonText;   // "TaoZip 解压"
    extern const wchar_t* const kCompressButtonText;  // "TaoZip 压缩"

    // Icon resource: TaoZip taiji icon embedded in this DLL (App icon reuse).
    // Resource index 1 per TaoZipShellext.rc.
    extern const wchar_t* const kIconReference;

    // Extension decision, mirroring C# ArchiveKinds.OpenableExtensions exactly.
    // Path forms: case-insensitive; supports .tar.gz / .tar.bz2 / .tar.xz compounds.
    bool IsArchivePath(const wchar_t* path);
    bool IsDirectoryLikePath(const wchar_t* path);

    // Decision entry: same semantics as C# ExplorerButtonPolicy.Decide.
    // 0 = None, 1 = Extract, 2 = Compress.
    int DecideButton(IShellItemArray* selection);

    // Common base: title/icon/state plumbing.
    class CommandBase : public IExplorerCommand
    {
    public:
        CommandBase(int buttonKind) noexcept : _buttonKind(buttonKind), _refCount(1) {}
        virtual ~CommandBase() = default;

        // IUnknown
        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
        IFACEMETHODIMP_(ULONG) AddRef() override;
        IFACEMETHODIMP_(ULONG) Release() override;

        // IExplorerCommand
        IFACEMETHODIMP GetTitle(IShellItemArray* psiItemArray, LPWSTR* ppszName) override;
        IFACEMETHODIMP GetIcon(IShellItemArray* psiItemArray, LPWSTR* ppszIcon) override;
        IFACEMETHODIMP GetToolTip(IShellItemArray* psiItemArray, LPWSTR* ppszInfotip) override;
        IFACEMETHODIMP GetCanonicalName(GUID* pguidCommandName) override;
        IFACEMETHODIMP GetState(IShellItemArray* psiItemArray, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState) override;
        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) override;
        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) override;
        IFACEMETHODIMP Invoke(IShellItemArray* psiItemArray, IBindCtx* pCtx) override = 0;

    protected:
        long _refCount;
        int _buttonKind;
        void LaunchTaoZip(IShellItemArray* selection, const wchar_t* switchName);
    };

    class ExtractCommand final : public CommandBase
    {
    public:
        ExtractCommand() noexcept : CommandBase(1) {}
        IFACEMETHODIMP Invoke(IShellItemArray* psiItemArray, IBindCtx* pCtx) override;
    };

    class CompressCommand final : public CommandBase
    {
    public:
        CompressCommand() noexcept : CommandBase(2) {}
        IFACEMETHODIMP Invoke(IShellItemArray* psiItemArray, IBindCtx* pCtx) override;
    };
}
