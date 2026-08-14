using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;

namespace StaminaManager.ViewModels;

public sealed partial class GameCardViewModel : OverviewItemViewModel
{
    private readonly Action<Guid>? _editRequested;
    private readonly Action<Guid>? _updateCurrentRequested;
    private GameEntry _entry;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string? ImageAssetId { get; set; }

    [ObservableProperty]
    public partial int CurrentStamina { get; set; }

    [ObservableProperty]
    public partial int MaxStamina { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial StaminaStatus Status { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? FullAtUtc { get; set; }

    [ObservableProperty]
    public partial TimeSpan Remaining { get; set; }

    public GameCardViewModel(
        GameEntry entry,
        DateTimeOffset nowUtc,
        Action<Guid>? editRequested = null,
        Action<Guid>? updateCurrentRequested = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;
        Name = entry.Name;
        ImageAssetId = entry.ImageAssetId;
        _editRequested = editRequested;
        _updateCurrentRequested = updateCurrentRequested;
        Refresh(entry, nowUtc);
    }

    public Guid Id => _entry.Id;

    public GameEntry Entry => _entry;

    public void Refresh(DateTimeOffset nowUtc) =>
        Refresh(_entry, nowUtc);

    public void Refresh(
        GameEntry entry,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Id != Id)
        {
            throw new ArgumentException(
                "別のゲームでカードを更新することはできません。",
                nameof(entry));
        }

        _entry = entry;
        StaminaSnapshot snapshot = StaminaCalculator.Calculate(
            entry,
            nowUtc);
        Name = entry.Name;
        ImageAssetId = entry.ImageAssetId;
        CurrentStamina = snapshot.Current;
        MaxStamina = snapshot.Maximum;
        Progress = snapshot.Ratio;
        Status = snapshot.Status;
        FullAtUtc = snapshot.FullAtUtc;
        Remaining = snapshot.Remaining;
        OnPropertyChanged(nameof(Entry));
    }

    [RelayCommand]
    private void Edit() => _editRequested?.Invoke(Id);

    [RelayCommand]
    private void UpdateCurrent() =>
        _updateCurrentRequested?.Invoke(Id);
}
