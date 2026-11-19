using System.Globalization;
using AracKiralama.Business.Services;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>
    /// One car's month: every day free, booked, in maintenance or past, as the database's today decides.
    /// Each day says its state in words and has a pattern of its own, so the colours only help and high
    /// contrast loses nothing. The cells are sized from the font, so large text never cuts a word.
    /// </summary>
    public sealed class AvailabilityCalendarForm : AppDialog
    {
        internal static readonly string[] DayNames = ["Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz"];

        private readonly IAvailabilityService _availability;
        private readonly UserSession _session;
        private readonly int _carId;
        private readonly RequestSequence _loads = new();
        private readonly DayCell[] _cells = new DayCell[42];
        private DateOnly? _shownMonth;

        /// <summary>The month of the latest request, which quick clicks count from before its answer arrives.</summary>
        private DateOnly? _requestedMonth;

        private readonly ThemedButton btnPreviousMonth = FormLayout.Button(nameof(btnPreviousMonth), "‹ Önceki ay", ButtonKind.Secondary);
        private readonly Label lblMonth = new() { Name = nameof(lblMonth), AutoSize = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleCenter, Text = "Takvim yükleniyor…" };
        private readonly ThemedButton btnNextMonth = FormLayout.Button(nameof(btnNextMonth), "Sonraki ay ›", ButtonKind.Secondary);
        private readonly ThemedButton btnClose = FormLayout.Button(nameof(btnClose), "Kapat", ButtonKind.Secondary);

        public AvailabilityCalendarForm(IAvailabilityService availability, UserSession session, int carId, string carTitle)
            : base("Müsaitlik takvimi", carTitle, Glyphs.Calendar, new Size(640, 640), new Size(560, 560), modal: true)
        {
            ArgumentNullException.ThrowIfNull(availability);
            ArgumentNullException.ThrowIfNull(session);

            _availability = availability;
            _session = session;
            _carId = carId;
            BuildLayout();

            Load += async (_, _) => await this.RunAsync(() => LoadMonthAsync(null));
            btnPreviousMonth.Click += async (_, _) => await this.RunAsync(() => MoveAsync(-1));
            btnNextMonth.Click += async (_, _) => await this.RunAsync(() => MoveAsync(1));
            btnClose.Click += (_, _) => Close();
        }

        /// <summary>The month shown now, for tests.</summary>
        internal CarCalendar? ShownCalendar { get; private set; }

        internal IReadOnlyList<Label> DayCells => _cells;

        /// <summary>
        /// Loads the month of <paramref name="month"/>, or with none the month of today by the database's clock;
        /// only the answer of the latest request is shown.
        /// </summary>
        internal async Task LoadMonthAsync(DateOnly? month)
        {
            var request = _loads.Next();
            _requestedMonth = month;
            CarCalendar calendar;
            try
            {
                calendar = await _availability.GetCalendarAsync(_session, _carId, month);
            }
            catch
            {
                if (_loads.IsLatest(request))
                {
                    // The month asked for was not shown; the next click counts from the one on the screen.
                    _requestedMonth = _shownMonth;
                }

                throw;
            }

            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            Show(calendar);
        }

        /// <summary>A day's text: its number and its state in words.</summary>
        internal static string Describe(CalendarDay day) =>
            day.Date.Day.ToString(CultureInfo.InvariantCulture) + Environment.NewLine + Word(day.State);

        /// <summary>A cell fits the day number and the widest state word, whatever the font or the scale.</summary>
        internal static Size CellSize(Font font)
        {
            ArgumentNullException.ThrowIfNull(font);
            var size = DayCell.SizeFor(font, font);

            // The number and the word are measured one by one; the test text puts them on two lines of one font.
            return new Size(size.Width, Math.Max(size.Height, TextRenderer.MeasureText("30" + Environment.NewLine + "Geçmiş", font).Height));
        }

        /// <summary>
        /// Asks for the month <paramref name="months"/> away from the latest one asked for (or shown), so quick
        /// clicks add up and the latest wins; nothing before the first month is known.
        /// </summary>
        internal Task MoveAsync(int months) =>
            (_requestedMonth ?? _shownMonth) is { } from ? LoadMonthAsync(from.AddMonths(months)) : Task.CompletedTask;

        private static string Word(DayState state) => state switch
        {
            DayState.Booked => "Dolu",
            DayState.Maintenance => "Bakım",
            DayState.Past => "Geçmiş",
            _ => "Boş",
        };

        private void Show(CarCalendar calendar)
        {
            ShownCalendar = calendar;
            _shownMonth = calendar.Month;
            _requestedMonth = calendar.Month;
            lblMonth.Text = calendar.Month.ToString("MMMM yyyy", DisplayFormat.Turkish);
            var offset = ((int)calendar.Month.DayOfWeek + 6) % 7; // Monday first
            SuspendLayout();
            for (var i = 0; i < _cells.Length; i++)
            {
                var index = i - offset;
                if (index < 0 || index >= calendar.Days.Count)
                {
                    _cells[i].Show(string.Empty, null, today: false);
                    continue;
                }

                var day = calendar.Days[index];
                _cells[i].Show(Describe(day), day.State, day.Date == calendar.Today);
            }

            ResumeLayout(true);
        }

        private static FlowLayoutPanel LegendItem(string name, DayState state, string text, bool today = false)
        {
            var label = FormLayout.Hint(text);
            label.Margin = new Padding(0, 0, Spacing.L, 0);
            label.Anchor = AnchorStyles.Left;
            var swatch = new DaySwatch(state, today) { Name = name + "Swatch", Anchor = AnchorStyles.Left };
            var item = new FlowLayoutPanel
            {
                Name = name,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, Spacing.S),
            };
            item.Controls.Add(swatch);
            item.Controls.Add(label);
            return item;
        }

        private void BuildLayout()
        {
            Name = nameof(AvailabilityCalendarForm);
            lblMonth.Font = Theme.Fonts.Title;
            lblMonth.ForeColor = Theme.Current.Text;
            btnPreviousMonth.Margin = btnNextMonth.Margin = Padding.Empty;
            var monthBar = FormLayout.Bar(
                "monthBar",
                (btnPreviousMonth, false),
                (FormLayout.Spacer("monthSpacerLeft"), true),
                (lblMonth, false),
                (FormLayout.Spacer("monthSpacerRight"), true),
                (btnNextMonth, false));
            monthBar.Margin = new Padding(0, 0, 0, Spacing.M);

            var grid = new TableLayoutPanel
            {
                Name = "tblDays",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 7,
                RowCount = 7,
                Anchor = AnchorStyles.Top,
                Margin = Padding.Empty,
            };
            for (var column = 0; column < 7; column++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var name = new Label
                {
                    Name = "lblDayName" + column.ToString(CultureInfo.InvariantCulture),
                    Text = DayNames[column],
                    AutoSize = true,
                    Font = Theme.Fonts.BodyStrong,
                    ForeColor = Theme.Current.TextSecondary,
                    Anchor = AnchorStyles.None,
                    Margin = new Padding(0, 0, 0, Spacing.XS),
                };
                grid.Controls.Add(name, column, 0);
            }

            for (var i = 0; i < _cells.Length; i++)
            {
                _cells[i] = new DayCell { Name = "lblDay" + i.ToString(CultureInfo.InvariantCulture), Anchor = AnchorStyles.None };
                grid.Controls.Add(_cells[i], i % 7, (i / 7) + 1);
            }

            var gridHost = FormLayout.Stack("calendarStack", monthBar, grid);
            grid.Dock = DockStyle.None;
            grid.Anchor = AnchorStyles.Top;
            var calendarCard = FormLayout.Card("calendarCard", null, gridHost, fill: false);

            var legend = new FlowLayoutPanel
            {
                Name = "pnlLegend",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
            };
            legend.Controls.Add(LegendItem("legendFree", DayState.Free, "Boş: kiralanabilir"));
            legend.Controls.Add(LegendItem("legendBooked", DayState.Booked, "Dolu: başka bir kiralamada"));
            legend.Controls.Add(LegendItem("legendMaintenance", DayState.Maintenance, "Bakım: bakımda veya hasar onarımında"));
            legend.Controls.Add(LegendItem("legendPast", DayState.Past, "Geçmiş: geçmiş gün"));
            legend.Controls.Add(LegendItem("legendToday", DayState.Free, "Bugün (veritabanının tarihi)", today: true));
            calendarCard.Margin = new Padding(0, 0, 0, Spacing.M);

            Content.Controls.Add(FormLayout.Stack("calendarPage", calendarCard, legend));

            CancelButton = btnClose;
            SetFooterNote("Dolu ve bakımdaki günler kiralanamaz.");
            SetFooterButtons(btnClose);
            FinishLayout();
        }
    }
}
