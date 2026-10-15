// The Windows status line bridge: StatusLineBridge.cs compiled to a small native exe,
// because Claude Code runs it after every reply and it must start instantly.
// macOS and Linux run the same code through Ringside itself (Ringside --statusline).

namespace Ringside.StatusLine;

static class Program
{
    static int Main() => Ringside.StatusLineBridge.Run();
}
