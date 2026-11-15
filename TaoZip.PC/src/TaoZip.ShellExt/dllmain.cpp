// dllmain.cpp - DLL entry + standard COM exports for the two commands.
// Standard exports (DllGetClassObject / DllCanUnloadNow) are what SurrogateServer
// loads the DLL through; without them the shell extension never activates.
#include "TaoZipCommands.h"

#include <objbase.h>
#include <new>

namespace TaoZip
{
    // CLSIDs must match TaoZipShellext.idl and the sparse-package manifest.
    static const GUID kExtractClsid =
    { 0xC1D2E3F4, 0x5A6B, 0x7C8D, { 0x9E, 0x0F, 0x1A, 0x2B, 0x3C, 0x4D, 0x5E, 0x6F } };
    static const GUID kCompressClsid =
    { 0xD2E3F4A5, 0x6B7C, 0x8D9E, { 0x0F, 0x1A, 0x2B, 0x3C, 0x4D, 0x5E, 0x6F, 0x7A } };

    static long g_lockCount = 0;

    // Class factory: one instance per CLSID request.
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

// ---- Standard COM exports (required by com:SurrogateServer activation) ----

__declspec(dllexport) HRESULT STDAPICALLTYPE DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    int kind = IsEqualCLSID(rclsid, TaoZip::kExtractClsid) ? 1
             : IsEqualCLSID(rclsid, TaoZip::kCompressClsid) ? 2
             : 0;
    if (kind == 0) return CLASS_E_CLASSNOTAVAILABLE;
    TaoZip::CommandFactory* factory = new (std::nothrow) TaoZip::CommandFactory(kind);
    if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

__declspec(dllexport) HRESULT STDAPICALLTYPE DllCanUnloadNow(void)
{
    return g_lockCount == 0 ? S_OK : S_FALSE;
}

BOOL APIENTRY DllMain(HMODULE hmod, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(hmod);
    return TRUE;
}

// regsvr32 fallback stubs: classic-menu registration stays with the MSI.
__declspec(dllexport) HRESULT __stdcall DllRegisterServer(void) { return S_OK; }
__declspec(dllexport) HRESULT __stdcall DllUnregisterServer(void) { return S_OK; }
