// adas-arch-shim.dll — lets DLSS 5 tools that host nvngx_dlssnr.dll in their own process (e.g.
// ThioJoe's Full-Screen-DLSS5-Wrapper) create DLSS 5 Neural Rendering on RTX 20/30/40 cards.
//
// Same principle as NeuralScreen's worker (native/dlss5-feed-host64.cpp, MIT): nvngx_dlssnr.dll
// asks nvapi64.dll for NvAPI_GPU_GetArchInfo and refuses anything below Blackwell, although its
// compiled kernels cover sm_75/86/89. The refusal is a policy check, not missing code. Adas starts
// the tool suspended, injects this DLL, waits for the patch, then resumes. The patch caches every
// card's real answer, then replaces the real GetArchInfo prologue in this process's memory with a
// jump to a hook that reports Blackwell (0x1B0) for the primary card only. No NVIDIA file is
// modified; on a Blackwell card the shim installs nothing.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <cstdarg>
#include <cstring>

static constexpr unsigned NVAPI_ID_INITIALIZE = 0x0150E828u;
static constexpr unsigned NVAPI_ID_ENUM_GPUS  = 0xE5AC921Fu;
static constexpr unsigned NVAPI_ID_GET_ARCH   = 0xD8265D24u;
static constexpr unsigned NV_ARCH_TURING = 0x160u, NV_ARCH_AMPERE = 0x170u, NV_ARCH_ADA = 0x190u;
static constexpr unsigned NV_ARCH_BLACKWELL = 0x1B0u;

struct NvArchInfo { unsigned version, architecture, implementation, revision; };
using PFN_NvQueryInterface = void *(__cdecl *)(unsigned);
using PFN_NvGetArchInfo = int (__cdecl *)(void *, NvArchInfo *);

static const int kMaxGpus = 64;
static void *g_handles[kMaxGpus];
static NvArchInfo g_cache[kMaxGpus];
static int g_count;

static void Log(const char *fmt, ...)
{
    char path[MAX_PATH] = {};
    DWORD n = GetModuleFileNameA(nullptr, path, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return;
    char *slash = strrchr(path, '\\');
    if (slash == nullptr) return;
    strcpy_s(slash + 1, MAX_PATH - (slash + 1 - path), "adas-arch-shim.log");
    FILE *f = nullptr;
    if (fopen_s(&f, path, "a") != 0 || f == nullptr) return;
    SYSTEMTIME t; GetLocalTime(&t);
    fprintf(f, "%02d:%02d:%02d.%03d  ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
    va_list args; va_start(args, fmt); vfprintf(f, fmt, args); va_end(args);
    fputc('\n', f);
    fclose(f);
}

static int __cdecl ArchInfoHook(void *gpu, NvArchInfo *info)
{
    if (info == nullptr) return -1;
    const unsigned want = info->version;
    int idx = 0;
    for (int i = 0; i < g_count; ++i)
        if (g_handles[i] == gpu) { idx = i; break; }
    *info = g_cache[idx];
    info->version = want;
    const unsigned group = info->architecture & 0xFFFFFFF0u;
    if (idx == 0 && (group == NV_ARCH_TURING || group == NV_ARCH_AMPERE || group == NV_ARCH_ADA))
    {
        info->architecture = NV_ARCH_BLACKWELL;
        info->implementation = 0x3u;
        info->revision = 0xA1u;
    }
    return 0;
}

static bool WriteCode(void *at, const void *src, size_t bytes)
{
    DWORD old = 0;
    if (!VirtualProtect(at, bytes, PAGE_EXECUTE_READWRITE, &old)) return false;
    memcpy(at, src, bytes);
    DWORD tmp = 0;
    VirtualProtect(at, bytes, old, &tmp);
    FlushInstructionCache(GetCurrentProcess(), at, bytes);
    return true;
}

// 1 = patched, 0 = not needed, negative = failed.
static int Setup()
{
    HMODULE nvapi = LoadLibraryW(L"nvapi64.dll");
    if (nvapi == nullptr) { Log("[arch] nvapi64.dll did not load, err=%lu", GetLastError()); return -1; }
    auto qi = reinterpret_cast<PFN_NvQueryInterface>(GetProcAddress(nvapi, "nvapi_QueryInterface"));
    if (qi == nullptr) { Log("[arch] no nvapi_QueryInterface"); return -2; }
    auto init = reinterpret_cast<int (__cdecl *)()>(qi(NVAPI_ID_INITIALIZE));
    auto enum_gpus = reinterpret_cast<int (__cdecl *)(void **, unsigned *)>(qi(NVAPI_ID_ENUM_GPUS));
    auto get_arch = reinterpret_cast<PFN_NvGetArchInfo>(qi(NVAPI_ID_GET_ARCH));
    if (init == nullptr || enum_gpus == nullptr || get_arch == nullptr) { Log("[arch] nvapi ids did not resolve"); return -3; }
    if (init() != 0) { Log("[arch] NvAPI_Initialize failed"); return -4; }

    void *handles[kMaxGpus] = {};
    unsigned count = 0;
    if (enum_gpus(handles, &count) != 0 || count == 0) { Log("[arch] no GPU list"); return -5; }
    if (count > static_cast<unsigned>(kMaxGpus)) count = kMaxGpus;
    for (unsigned i = 0; i < count; ++i)
    {
        NvArchInfo one = {};
        one.version = sizeof(NvArchInfo) | (2u << 16);
        if (get_arch(handles[i], &one) != 0)
        {
            one.version = sizeof(NvArchInfo) | (1u << 16);
            if (get_arch(handles[i], &one) != 0) continue;
        }
        g_handles[g_count] = handles[i];
        g_cache[g_count++] = one;
    }
    if (g_count == 0) { Log("[arch] GetArchInfo answered for no card"); return -6; }

    const unsigned real = g_cache[0].architecture;
    if ((real & 0xFFFFFFF0u) >= NV_ARCH_BLACKWELL) { Log("[arch] architecture 0x%X is supported - no spoof needed", real); return 0; }

    BYTE code[12] = { 0x48, 0xB8 };
    void *dst = reinterpret_cast<void *>(&ArchInfoHook);
    memcpy(code + 2, &dst, sizeof(dst));
    code[10] = 0xFF; code[11] = 0xE0;
    if (!WriteCode(reinterpret_cast<void *>(get_arch), code, sizeof(code))) { Log("[arch] patch failed, err=%lu", GetLastError()); return -7; }
    Log("[arch] patch installed: %d cards cached, architecture 0x%X spoofed to 0x%X", g_count, real, NV_ARCH_BLACKWELL);
    return 1;
}

static DWORD WINAPI SetupThread(LPVOID)
{
    int result = Setup();
    wchar_t name[64];
    swprintf_s(name, L"Local\\AdasArchShim-%lu", GetCurrentProcessId());
    if (HANDLE done = OpenEventW(EVENT_MODIFY_STATE, FALSE, name)) { SetEvent(done); CloseHandle(done); }
    return static_cast<DWORD>(result);
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        // Runs once the loader lock is released; Adas keeps the tool suspended until the event fires.
        if (HANDLE t = CreateThread(nullptr, 0, SetupThread, nullptr, 0, nullptr)) CloseHandle(t);
    }
    return TRUE;
}
