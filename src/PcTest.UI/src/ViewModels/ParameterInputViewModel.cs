using PcTest.Contracts.Manifest;

namespace PcTest.UI.ViewModels;

/// <summary>
/// Captures user-provided parameter values for a manifest definition.
/// </summary>
public class ParameterInputViewModel : ViewModelBase
{
    private string _value = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParameterInputViewModel"/> class.
    /// </summary>
    public ParameterInputViewModel(ParameterDefinition definition, string? defaultValue)
    {
        Definition = definition;
        DefaultValue = defaultValue;
    }

    /// <summary>
    /// Underlying manifest definition.
    /// </summary>
    public ParameterDefinition Definition { get; }

    /// <summary>
    /// Optional formatted default value for display.
    /// </summary>
    public string? DefaultValue { get; }

    /// <summary>
    /// Value entered by the user.
    /// </summary>
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    /// <summary>
    /// Label used in the UI.
    /// </summary>
    public string Label => Definition.Required ? $"{Definition.Name} *" : Definition.Name;

    /// <summary>
    /// Hint showing the expected type and default.
    /// </summary>
    public string Hint => string.IsNullOrWhiteSpace(DefaultValue)
        ? Definition.Type
        : $"{Definition.Type} (default: {DefaultValue})";
}
