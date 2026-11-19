using System.Globalization;
using AracKiralama.Business.Services;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>
    /// One car's month: every day free, booked, in maintenance or past, as the database's today decides.
    /// Each day says its state in words, so the colours only help and high contrast loses nothing. The
    /// layout follows the font: the cells are sized from the font's height and the form sizes itself.
    /// </summary>
    public sealed class AvailabilityCalendarForm : Form
    {
        internal static readonly string[] DayNames = ["Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz"];

        private static readonly string[] CellWords = ["Geçmiş", "Bakım", "Dolu", "Boş", "30"];

        private readonly IAvailabilityService _availability;
        private readonly UserSession _session;
        private readonly int _carId;
        private readonly RequestSequence _loads = new();
        private readonly Label _month;
        private readonly TableLayoutPanel _grid;
        private readonly Label[] _cells = new Label[42];
        private DateOnly _shownMonth;

        public AvailabilityCalendarForm(IAvailabilityService availability, UserSession session, int carId, string carTitle)
        {
            ArgumentNullException.ThrowIfNull(availability);
            ArgumentNullException.ThrowIfNull(session);

            _availability = availability;
            _session = session;
            _carId = carId;
            _shownMonth = DateOnly.FromDateTime(DateTime.Today);

            Text = "Müsaitlik Takvimi - " + carTitle;
            Icon = AppAssets.Icon;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            var previous = new Button { Name = "btnPreviousMonth", Text = "< Önceki ay", AutoSize = true };
            var next = new Button { Name = "btnNextMonth", Text = "Sonraki ay >", AutoSize = true };
            _month = new Label { Name = "lblMonth", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(12, 0, 12, 0), Font = new Font(Font, FontStyle.Bold) };
            previous.Click += async (_, _) => await this.RunAsync(() => LoadMonthAsync(_shownMonth.AddMonths(-1)));
            next.Click += async (_, _) => await this.RunAsync(() => LoadMonthAsync(_shownMonth.AddMonths(1)));
            var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
            bar.Controls.AddRange([previous, _month, next]);

            _grid = new TableLayoutPanel { Name = "tblDays", AutoSize = true, ColumnCount = 7, RowCount = 7, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
            var cellSize = CellSize(Font);
            for (var column = 0; column < 7; column++)
            {
                _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                _grid.Controls.Add(new Label { Text = DayNames[column], AutoSize = false, Size = new Size(cellSize.Width, Font.Height + 6), TextAlign = ContentAlignment.MiddleCenter, Margin = Padding.Empty }, column, 0);
            }

            for (var i = 0; i < _cells.Length; i++)
            {
                _cells[i] = new Label { Name = "lblDay" + i.ToString(CultureInfo.InvariantCulture), AutoSize = false, Size = cellSize, TextAlign = ContentAlignment.MiddleCenter, Margin = Padding.Empty };
                _grid.Controls.Add(_cells[i], i % 7, (i / 7) + 1);
            }

            var legend = new Label
            {
                Name = "lblLegend",
                AutoSize = true,
                MaximumSize = new Size(cellSize.Width * 7, 0),
                Margin = new Padding(0, 8, 0, 0),
                Text = "Boş: kiralanabilir. Dolu: başka bir kiralamada. Bakım: bakımda veya hasar onarımında. Geçmiş: geçmiş gün.",
            };

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            root.Controls.Add(bar, 0, 0);
            root.Controls.Add(_grid, 0, 1);
            root.Controls.Add(legend, 0, 2);
            Controls.Add(root);
        }

        /// <summary>The month shown now, for tests.</summary>
        internal CarCalendar? ShownCalendar { get; private set; }

        internal IReadOnlyList<Label> DayCells => _cells;

        /// <summary>Loads the month of <paramref name="month"/>; only the answer of the latest request is shown.</summary>
        internal async Task LoadMonthAsync(DateOnly month)
        {
            var request = _loads.Next();
            var calendar = await _availability.GetCalendarAsync(_session, _carId, month);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            Show(calendar);
        }

        /// <summary>A day's text: its number and its state in words.</summary>
        internal static string Describe(CalendarDay day) =>
            day.Date.Day.ToString(CultureInfo.InvariantCulture) + Environment.NewLine + day.State switch
            {
                DayState.Booked => "Dolu",
                DayState.Maintenance => "Bakım",
                DayState.Past => "Geçmiş",
                _ => "Boş",
            };

        /// <summary>A cell fits two lines of the form's font and the widest state word, whatever the font or the scale.</summary>
        internal static Size CellSize(Font font)
        {
            var widest = CellWords.Max(w => TextRenderer.MeasureText(w, font).Width);
            return new Size(widest + font.Height, (font.Height * 2) + font.Height);
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(() => LoadMonthAsync(_shownMonth));
        }

        private void Show(CarCalendar calendar)
        {
            ShownCalendar = calendar;
            _shownMonth = calendar.Month;
            _month.Text = calendar.Month.ToString("MMMM yyyy", DisplayFormat.Turkish);
            var offset = ((int)calendar.Month.DayOfWeek + 6) % 7; // Monday first
            var highContrast = SystemInformation.HighContrast;
            for (var i = 0; i < _cells.Length; i++)
            {
                var index = i - offset;
                var cell = _cells[i];
                if (index < 0 || index >= calendar.Days.Count)
                {
                    cell.Text = string.Empty;
                    cell.ResetBackColor();
                    continue;
                }

                var day = calendar.Days[index];
                cell.Text = Describe(day);

                // Only the background marks the state; the text keeps the system colour, and under high
                // contrast the system colours stay as they are.
                if (highContrast)
                {
                    cell.ResetBackColor();
                    continue;
                }

                cell.BackColor = day.State switch
                {
                    DayState.Booked => Color.FromArgb(248, 215, 218),
                    DayState.Maintenance => Color.FromArgb(255, 236, 179),
                    DayState.Past => Color.FromArgb(233, 236, 239),
                    _ => Color.FromArgb(212, 237, 218),
                };
            }
        }
    }
}
