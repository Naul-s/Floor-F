using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FloorF.Tests.EditMode")]
[assembly: InternalsVisibleTo("FloorF.Tests.PlayMode")]
[assembly: InternalsVisibleTo("FloorF.Editor")]
// Gameplay binds Core to Unity; it needs the internal debug entry points (GameSession.Debug.cs) for its DebugCheats
[assembly: InternalsVisibleTo("FloorF.Gameplay")]
