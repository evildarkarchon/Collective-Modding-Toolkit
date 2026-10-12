/*
 * Every F4SE plugin stand-in the F4SE scenarios commit (F4SE-2..F4SE-6, T-5). The F4SE tab never runs a plugin: it
 * maps each DLL with LoadLibraryExW(DONT_RESOLVE_DLL_REFERENCES), asks GetProcAddress for the four F4SE exports, and
 * reads the F4SEPlugin_Version data export (utils.parse_dll). So a stub only needs the right export table and, where it
 * exports one, the right version struct; the functions are never called.
 *
 * build.ps1 compiles this once per stub with a generated variant.h that defines which exports exist (EXPORT_LOAD,
 * EXPORT_PRELOAD, EXPORT_QUERY, EXPORT_VERSION, EXPORT_OTHER) and, for the version struct, PLUGIN_NAME,
 * ADDRESS_INDEPENDENCE, STRUCTURE_INDEPENDENCE and COMPATIBLE_VERSIONS (a brace-less initializer list).
 *
 * No CRT, /NOENTRY: the stubs stay tiny, and /Brepro keeps every rebuild byte-identical.
 */

#include "variant.h"

/*
 * F4SE's PluginVersionData (f4se/PluginAPI.h), as the reference declares it (utils.F4SEPluginVersionData): 1116 bytes,
 * with compatibleVersions at offset 528. The reference reads addressIndependence, structureIndependence and all 16
 * compatibleVersions entries, zeros included.
 */
typedef struct
{
    unsigned int dataVersion;
    unsigned int pluginVersion;
    char name[256];
    char author[256];
    unsigned int addressIndependence;
    unsigned int structureIndependence;
    unsigned int compatibleVersions[16];
    unsigned int seVersionRequired;
    unsigned int reservedNonBreaking;
    unsigned int reservedBreaking;
    unsigned char reserved[512];
} F4SEPluginVersionData;

#ifdef EXPORT_VERSION
__declspec(dllexport) F4SEPluginVersionData F4SEPlugin_Version = {
    1,
    1,
    PLUGIN_NAME,
    "Collective Modding Toolkit parity stub (not a real plugin)",
    ADDRESS_INDEPENDENCE,
    STRUCTURE_INDEPENDENCE,
    {COMPATIBLE_VERSIONS},
    0,
};
#endif

#ifdef EXPORT_QUERY
__declspec(dllexport) int F4SEPlugin_Query(const void *f4se, void *info)
{
    return 0;
}
#endif

#ifdef EXPORT_LOAD
__declspec(dllexport) int F4SEPlugin_Load(const void *f4se)
{
    return 0;
}
#endif

#ifdef EXPORT_PRELOAD
__declspec(dllexport) int F4SEPlugin_Preload(const void *f4se)
{
    return 0;
}
#endif

#ifdef EXPORT_OTHER
/* An export that is none of F4SE's, for a DLL that isn't a plugin but still has an export table. */
__declspec(dllexport) int NotAnF4sePlugin(void)
{
    return 0;
}
#endif
