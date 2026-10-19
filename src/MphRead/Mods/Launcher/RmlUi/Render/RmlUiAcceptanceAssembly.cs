#if MPHREAD_RMLUI_GL_SMOKE && !ANDROID && !MPHREAD_SERVER
using System.Runtime.CompilerServices;

// Keep renderer lifetime, capture and Community ownership internals private to
// the game; only these acceptance executables may inspect them.
[assembly: InternalsVisibleTo("rmlui-opengl-check")]
[assembly: InternalsVisibleTo("rmlui-community-revisions-check")]
#endif
