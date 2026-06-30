using System.Collections.ObjectModel;

namespace UmkaSharp;

/// <summary>Configures creation of an embedded Umka runtime.</summary>
public sealed class UmkaRuntimeOptions
{
    private readonly int _stackSize = UmkaRuntime.DefaultStackSize;
    private readonly ReadOnlyCollection<string>? _arguments;

    /// <summary>Initializes runtime creation options with secure embedding defaults.</summary>
    public UmkaRuntimeOptions()
    {
    }

    /// <summary>Gets or sets the Umka stack size, in slots.</summary>
    public int StackSize
    {
        get => _stackSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _stackSize = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether Umka file-system support is enabled.</summary>
    public bool FileSystemEnabled { get; init; }

    /// <summary>Gets or sets a value indicating whether Umka implementation library loading is enabled.</summary>
    public bool ImplementationLibrariesEnabled { get; init; }

    /// <summary>Gets or sets host-defined command-line arguments passed to Umka.</summary>
    public IReadOnlyList<string>? Arguments
    {
        get => _arguments;
        init => _arguments = ValidateArguments(value);
    }

    /// <summary>Gets or sets a callback invoked for Umka compile-time warnings.</summary>
    public Action<UmkaError>? WarningHandler { get; init; }

    private static ReadOnlyCollection<string>? ValidateArguments(IReadOnlyList<string>? arguments)
    {
        if (arguments is null)
            return null;

        var snapshot = new string[arguments.Count];
        for (var i = 0; i < snapshot.Length; i++)
        {
            var argument = arguments[i];
            if (argument is null)
                throw new ArgumentException("Command-line arguments cannot contain null values.", nameof(arguments));

            UmkaStringValidation.ThrowIfContainsNullCharacter(argument, nameof(arguments));
            snapshot[i] = argument;
        }

        return Array.AsReadOnly(snapshot);
    }
}
