using RT.Util.Forms;

namespace RunLogged;

public partial class PauseForDlg : ManagedForm
{
    public enum IntervalType
    {
        Seconds,
        Minutes,
        Hours,
        Days,
        Indefinitely
    }

    public class Interval : IEquatable<Interval>, IComparable<Interval>
    {
        public IntervalType Type;
        public double Length;
        public override string ToString()
        {
            return Type == IntervalType.Indefinitely ? "Indefinitely" : Length + " " + Type;
        }

        public bool Equals(Interval other)
        {
            return other != null && other.Type == Type && (Type == IntervalType.Indefinitely || other.Length == Length);
        }

        public int CompareTo(Interval other)
        {
            var c = Type.CompareTo(other.Type);
            if (c != 0)
                return c;
            return Length.CompareTo(other.Length);
        }
    }

    public new class Settings : ManagedForm.Settings
    {
        public List<Interval> Intervals = new List<Interval>
        {
            new Interval { Length = 1, Type = IntervalType.Minutes },
            new Interval { Length = 2, Type = IntervalType.Minutes },
            new Interval { Length = 5, Type = IntervalType.Minutes },
            new Interval { Length = 10, Type = IntervalType.Minutes },
            new Interval { Length = 15, Type = IntervalType.Minutes },
            new Interval { Length = 20, Type = IntervalType.Minutes },
            new Interval { Length = 30, Type = IntervalType.Minutes },
            new Interval { Length = 45, Type = IntervalType.Minutes },
            new Interval { Length = 1, Type = IntervalType.Hours },
            new Interval { Length = 2, Type = IntervalType.Hours },
            new Interval { Length = 4, Type = IntervalType.Hours },
            new Interval { Length = 8, Type = IntervalType.Hours },
            new Interval { Length = 12, Type = IntervalType.Hours },
            new Interval { Length = 18, Type = IntervalType.Hours },
            new Interval { Length = 1, Type = IntervalType.Days },
            new Interval { Length = 2, Type = IntervalType.Days },
            new Interval { Length = 4, Type = IntervalType.Days },
            new Interval { Length = 7, Type = IntervalType.Days },
            new Interval { Length = 14, Type = IntervalType.Days },
            new Interval { Length = 30, Type = IntervalType.Days },
            new Interval { Type = IntervalType.Indefinitely },
        };
        public Interval LastSelectedInterval;
    }

    private Settings _settings;

    public PauseForDlg(Settings settings)
        : base(settings)
    {
        _settings = settings;
        InitializeComponent();
        if (Program.ProgramIcon != null)
            Icon = Program.ProgramIcon;

        foreach (var val in Enum.GetValues(typeof(IntervalType)))
            cmbPauseFor.Items.Add(val);
        foreach (var item in _settings.Intervals)
            ctList.Items.Add(item);

        Interval sel = _settings.LastSelectedInterval ?? _settings.Intervals[0];
        cmbPauseFor.SelectedItem = sel.Type;
        numPauseFor.Value = (decimal) sel.Length;
    }

    private void ok(object _ = null, EventArgs __ = null)
    {
        _settings.LastSelectedInterval = new Interval { Length = (double) numPauseFor.Value, Type = (IntervalType) cmbPauseFor.SelectedItem };
        _settings.Intervals = ctList.Items.Cast<Interval>().ToList();
        DialogResult = DialogResult.OK;
    }

    private void ctList_MouseDoubleClick(object sender, MouseEventArgs e)
    {
        ok();
    }

    private void comboBoxChanged(object sender, EventArgs e)
    {
        numPauseFor.Enabled = (IntervalType) cmbPauseFor.SelectedItem != IntervalType.Indefinitely;
    }

    public TimeSpan TimeSpan
    {
        get
        {
            switch (_settings.LastSelectedInterval.Type)
            {
                case IntervalType.Seconds:
                    return TimeSpan.FromSeconds(_settings.LastSelectedInterval.Length);
                case IntervalType.Minutes:
                    return TimeSpan.FromMinutes(_settings.LastSelectedInterval.Length);
                case IntervalType.Hours:
                    return TimeSpan.FromHours(_settings.LastSelectedInterval.Length);
                case IntervalType.Days:
                    return TimeSpan.FromDays(_settings.LastSelectedInterval.Length);
                case IntervalType.Indefinitely:
                default:
                    return TimeSpan.FromMilliseconds(-1);
            }
        }
    }

    public bool IsIndefinite { get { return _settings.LastSelectedInterval.Type == IntervalType.Indefinitely; } }

    private void listChangeSelection(object sender, EventArgs e)
    {
        if (ctList.SelectedIndex == -1)
            return;
        var sel = (Interval) ctList.SelectedItem;
        if (sel.Type != IntervalType.Indefinitely)
            numPauseFor.Value = (decimal) sel.Length;
        cmbPauseFor.SelectedItem = sel.Type;
    }

    private void numPauseForEntered(object sender, EventArgs e)
    {
        numPauseFor.Select(0, numPauseFor.Text.Length);
    }

    private void add(object sender, EventArgs e)
    {
        var newInterval = new Interval
        {
            Type = (IntervalType) cmbPauseFor.SelectedItem,
            Length = (double) numPauseFor.Value
        };
        var index = 0;
        while (index < ctList.Items.Count && ((Interval) ctList.Items[index]).CompareTo(newInterval) < 0)
            index++;
        ctList.Items.Insert(index, newInterval);
    }

    private void remove(object sender, EventArgs e)
    {
        if (ctList.SelectedIndex != -1)
            ctList.Items.RemoveAt(ctList.SelectedIndex);
    }
}
