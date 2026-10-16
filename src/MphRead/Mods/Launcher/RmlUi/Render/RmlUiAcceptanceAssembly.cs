#if MPHREAD_RMLUI_GL_SMOKE && !ANDROID && !MPHREAD_SERVER
using System.Runtime.CompilerServices;

// Keep renderer lifetime and capture internals private to the game; only the
// opt-in real-GL acceptance executable may inspect them.
[assembly: InternalsVisibleTo("rmlui-opengl-check")]
#endif
