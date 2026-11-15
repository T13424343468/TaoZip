// dllmain.cpp - DLL entry + class factory for the two commands.
#include "TaoZipCommands.h"

#include <objbase.h>

namespace TaoZip
{
    static long g_lockCount = 0;

    // Class factory: single instance per CLSID.
    class CommandFactory final : public IClassFactory
    {
    public:
        CommandFactory(int buttonKind) noexcept : _buttonKind(buttonKind), _refCount(1) {}

        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            if (!ppv) return E_POINTER;
            if (riid == __uuidof(IUnknown) || riid == __uuidof(IClassFactory))
            {
                *ppv = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }
            *ppv = nullptr;
            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refCount); }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            ULONG c = InterlockedDecrement(&_refCount);
            if (c == 0) delete this;
            return c;
        }

        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
        {
            if (outer) return CLASS_E_NOAGGREGATION;
            CommandBase* cmd = (_buttonKind == 1)
                ? static_cast<CommandBase*>(new (std::nothrow) ExtractCommand())
                : static_cast<CommandBase*>(new (std::nothrow) CompressCommand());
            if (!cmd) return E_OUTOFMEMORY;
            HRESULT hr = cmd->QueryInterface(riid, ppv);
            cmd->Release();
            return hr;
        }

        IFACEMETHODIMP LockServer(BOOL fLock) override
        {
            if (fLock) InterlockedIncrement(&g_lockCount);
            else InterlockedDecrement(&g_lockCount);
            return S_OK;
        }

    private:
        int _buttonKind;
        long _refCount;
    };
}

extern "C" __declspec(dllexport) HRESULT STDAPICALLTYPE DllGetCommandFactory(int buttonKind, REFIID riid, void** ppv)
{
    TaoZip::CommandFactory* f = new (std::nothrow) TaoZip::CommandFactory(buttonKind);
    if (!f) return E_OUTOFMEMORY;
    HRESULT hr = f->QueryInterface(riid, ppv);
    f->Release();
    return hr;
}

static HRESULT RegisterCommand(HKEY root, const wchar_t* clsid, const wchar_t* text, const wchar_t* iconPath)
{
    wchar_t key[256];
    swprintf_s(key, 256, L"Software\\Classes\\TaoZip.%s\\ShellExt", text); // placeholder (wired below)
    return S_OK;
}

BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(GetModuleHandleW(nullptr));
    return TRUE;
}

// Exported for regsvr32-based classic menu fallback registration.
__declspec(dllexport) HRESULT __stdcall DllRegisterServer(void) { return S_OK; }
__declspec(dllexport) HRESULT __stdcall DllUnregisterServer(void) { return S_OK; }
