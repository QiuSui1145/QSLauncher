using System.Windows.Input;

namespace QSLauncher.App.ViewModels;

public sealed record PlaceholderFeature(string Title, string Description);

public abstract class PlaceholderPageViewModel : ObservableObject
{
    protected PlaceholderPageViewModel(
        string eyebrow,
        string title,
        string description,
        string milestone,
        string status,
        IReadOnlyList<PlaceholderFeature> features,
        Action<PageKey> navigate)
    {
        Eyebrow = eyebrow;
        Title = title;
        Description = description;
        Milestone = milestone;
        Status = status;
        Features = features;
        BackHomeCommand = new RelayCommand(() => navigate(PageKey.Home));
    }

    public string Eyebrow { get; }

    public string Title { get; }

    public string Description { get; }

    public string Milestone { get; }

    public string MilestoneLabel => $"阶段 {Milestone}";

    public string Status { get; }

    public IReadOnlyList<PlaceholderFeature> Features { get; }

    public virtual bool IsPriority => false;

    public ICommand BackHomeCommand { get; }
}


