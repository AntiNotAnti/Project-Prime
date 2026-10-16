#if MPHREAD_RMLUI_GL_SMOKE && !ANDROID && !MPHREAD_SERVER
using System.Runtime.CompilerServices;

// Only the native RmlUi software-frame acceptance assembly inspects game
// renderer internals. Keep all production graphics ownership non-public.
[assembly: InternalsVisibleTo("rmlui-opengl-check")]
#endif
