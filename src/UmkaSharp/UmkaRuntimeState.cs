namespace UmkaSharp;

/// <summary>Describes the observed lifecycle state of an embedded Umka runtime.</summary>
public enum UmkaRuntimeState
{
    /// <summary>The runtime has been created but compilation has not been attempted.</summary>
    Created = 0,

    /// <summary>Compilation was attempted but did not complete successfully.</summary>
    CompileAttempted = 1,

    /// <summary>The runtime has compiled successfully and can still execute code.</summary>
    Compiled = 2,

    /// <summary>The runtime has terminated after a native runtime error or script termination.</summary>
    Terminated = 3,

    /// <summary>The managed runtime wrapper has been disposed.</summary>
    Disposed = 4
}
