using CommunityToolkit.Mvvm.ComponentModel;

namespace XPXel.UI.ViewModels;

/// <summary>
/// The base class for every view model in XP-xel.
/// </summary>
/// <remarks>
/// <para>
/// It adds nothing to CommunityToolkit's <see cref="ObservableObject"/> today and exists so
/// that cross-cutting behaviour — a busy flag, error reporting, disposal — has an obvious
/// home later without every view model needing to change base class at that point.
/// </para>
/// <para>
/// <b>Why CommunityToolkit.Mvvm and not ReactiveUI.</b> Its <c>[ObservableProperty]</c> and
/// <c>[RelayCommand]</c> attributes are compile-time source generators: no runtime
/// reflection, no expression trees. That is what keeps the WebAssembly build trimmable and
/// leaves NativeAOT available for the desktop build later. It is also the option with the
/// least ceremony, which matters for a project that wants contributors.
/// </para>
/// </remarks>
public abstract class ViewModelBase : ObservableObject;
