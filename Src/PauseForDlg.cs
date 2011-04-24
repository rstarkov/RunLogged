using System;
using System.Windows.Forms;
using RT.Util.Forms;

namespace RunLogged
{
    public partial class PauseForDlg : ManagedForm
    {
        public enum IntervalType
        {
            Seconds,
            Minutes,
            Hours,
            Days,
            Forever
        }

        public new class Settings : ManagedForm.Settings
        {
            public IntervalType IntervalType = IntervalType.Minutes;
            public decimal Interval = 10;
        }

        private Settings _settings;

        public PauseForDlg(Settings settings)
            : base(settings)
        {
            _settings = settings;
            InitializeComponent();

            foreach (var val in Enum.GetValues(typeof(IntervalType)))
                cmbPauseFor.Items.Add(val);
            cmbPauseFor.SelectedItem = settings.IntervalType;
            numPauseFor.Value = settings.Interval;
        }

        private void ok(object sender, EventArgs e)
        {
            _settings.Interval = numPauseFor.Value;
            _settings.IntervalType = (IntervalType) cmbPauseFor.SelectedItem;
            DialogResult = DialogResult.OK;
        }

        private void comboBoxChanged(object sender, EventArgs e)
        {
            numPauseFor.Enabled = (IntervalType) cmbPauseFor.SelectedItem != IntervalType.Forever;
        }
    }
}
