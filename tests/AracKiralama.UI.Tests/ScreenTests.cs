using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Tests.Support;
using AracKiralama.UI.Theming;
using static AracKiralama.UI.Tests.Support.StaThread;

namespace AracKiralama.UI.Tests;

/// <summary>
/// Every screen is built with test doubles and laid out without being shown: window handles are created
/// for real layout and tab order, but no window becomes visible and no message window opens.
/// </summary>
public sealed class ScreenTests
{
    public static TheoryData<string> ScreenNames => [.. TestForms.Names];

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_HasTheApplicationIdentity(string name) => Run(() =>
    {
        using var form = TestForms.Create(name);

        Assert.StartsWith(AppText.Title + " - ", form.Text, StringComparison.Ordinal);
        Assert.Same(AppAssets.Icon, form.Icon);
        Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
        Assert.True(form.MinimumSize.Width > 0 && form.MinimumSize.Height > 0, "A resizable screen needs a minimum size.");
        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        Assert.Null(form.BackgroundImage);
        Assert.False(form.Visible);
    });

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_UsesThemedControlsAndAKeyboardWayOut(string name) => Run(() =>
    {
        using var form = TestForms.Create(name);
        var controls = Descendants(form).ToList();

        var buttons = controls.OfType<Button>().ToList();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.IsType<ThemedButton>(button));
        Assert.All(buttons, button => Assert.False(string.IsNullOrWhiteSpace(button.Text), $"{button.Name} has no text."));
        Assert.All(controls.OfType<ComboBox>(), combo => Assert.IsType<ThemedComboBox>(combo));
        Assert.All(controls.OfType<ListBox>(), list => Assert.IsType<ThemedListBox>(list));
        Assert.All(controls.OfType<CheckBox>(), check => Assert.IsType<ThemedCheckBox>(check));
        Assert.All(controls.OfType<DateTimePicker>(), date => Assert.IsType<DateField>(date.Parent));
        Assert.All(controls.OfType<TextBox>(), box => Assert.IsType<TextField>(box.Parent));
        Assert.Empty(controls.OfType<DataGridView>());
        Assert.All(controls.OfType<ThemedListBox>(), list => Assert.False(string.IsNullOrEmpty(list.EmptyText), $"{list.Name} has no empty text."));
        Assert.All(controls.OfType<CardGrid>(), grid => Assert.False(string.IsNullOrEmpty(grid.EmptyText), $"{grid.Name} has no empty text."));
        Assert.NotNull(form.CancelButton); // Esc closes every screen (a main panel asks to sign out).
    });

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_LaysOutInsideItsWindow(string name) => Run(() =>
    {
        using var form = TestForms.Create(name);

        LayoutAssert.FitsInside(form);
        Assert.False(form.Visible);
    });

    public static TheoryData<string, int, int, float> ScreensOnSmallScreens
    {
        get
        {
            var data = new TheoryData<string, int, int, float>();
            foreach (var (width, height) in new[] { (1024, 600), (1366, 768) })
            {
                foreach (var scale in LayoutAssert.FontScales)
                {
                    foreach (var name in TestForms.Names)
                    {
                        data.Add(name, width, height, scale);
                    }
                }
            }

            return data;
        }
    }

    /// <summary>
    /// On a small working area and with larger text the window still opens inside the screen, its minimum size
    /// too, and every control stays reachable: the body scrolls while the header and the footer stay in view,
    /// also while a two-line message is shown.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScreensOnSmallScreens))]
    public void Screen_FitsTheWorkingAreaAndKeepsEveryControlReachable(string name, int screenWidth, int screenHeight, float fontScale) => Run(() =>
    {
        var previous = Theme.Current;
        var previousScale = Theme.RenderScale;
        var previousArea = AppDialog.WorkingAreaOf;
        Theme.Use(previous, fontScale);
        var area = new Rectangle(0, 0, screenWidth, screenHeight);
        AppDialog.WorkingAreaOf = _ => area;
        try
        {
            using var form = TestForms.Create(name);
            LayoutAssert.CreateHandles(form);

            Assert.True(form.Width <= area.Width && form.Height <= area.Height, $"{name} opens at {form.Size}, larger than the working area {area.Size}.");
            Assert.True(form.MinimumSize.Width <= area.Width && form.MinimumSize.Height <= area.Height, $"{name} requires {form.MinimumSize}, larger than the working area {area.Size}.");

            foreach (var size in new[] { form.Size, form.MinimumSize })
            {
                form.Size = size;
                form.ShowStatus(StatusKind.Warning, "Lütfen bir araç seçin." + Environment.NewLine + "İkinci satır.");
                form.PerformLayout();

                try
                {
                    LayoutAssert.FitsInside(form);
                }
                catch (Xunit.Sdk.XunitException ex)
                {
                    throw new Xunit.Sdk.XunitException($"{name} at {form.Size}: {ex.Message}");
                }

                form.ClearStatus();
            }
        }
        finally
        {
            AppDialog.WorkingAreaOf = previousArea;
            Theme.Use(previous, previousScale);
        }
    });

    [Theory]
    [InlineData(nameof(LoginForm), "txtUserName", "btnExit")]
    [InlineData(nameof(RegisterForm), "txtUserName", "btnCancel")]
    [InlineData(nameof(FirstAdminSetupForm), "txtKey", "btnCancel")]
    [InlineData(nameof(ChangePasswordForm), "txtCurrentPassword", "btnCancel")]
    [InlineData(nameof(CarListForm), "cmbBrand", "btnSignOut")]
    [InlineData(nameof(RentalForm), "dtpStart", "btnBack")]
    [InlineData(nameof(ProfileForm), "btnContract8", "btnClose")]
    [InlineData(nameof(RentedCarsPanel), "cmbBrand", "btnClose")]
    [InlineData(nameof(AdminPanel), "btnRefresh", "btnSignOut")]
    [InlineData(nameof(UserManagementPanel), "btnNewUser", "btnClose")]
    [InlineData(nameof(AvailabilityCalendarForm), "btnPreviousMonth", "btnClose")]
    [InlineData(nameof(MaintenanceForm), "lstRecords", "btnClose")]
    [InlineData(nameof(RevenueReportForm), "dtpFrom", "btnClose")]
    public void TabOrder_StartsAtTheFirstInputAndEndsInTheFooter(string name, string first, string last) => Run(() =>
    {
        using var form = TestForms.Create(name);
        LayoutAssert.CreateHandles(form);

        var order = TabOrder(form);

        Assert.Equal(first, order[0].Name);
        Assert.Equal(last, order[^1].Name);
        Assert.Equal(order.Count, order.Distinct().Count());
    });

    [Theory]
    [InlineData(nameof(LoginForm), new[] { "txtUserName", "txtPassword", "btnSignIn", "btnRegister", "btnSetup", "btnExit" })]
    [InlineData(nameof(RentalForm), new[] { "dtpStart", "btnDecrease", "txtDays", "btnIncrease", "btnCalendar", "btnConfirm", "btnBack" })]
    [InlineData(nameof(AdminPanel), new[]
    {
        "btnRefresh", "lstCars", "txtBrand", "txtModel", "cmbYear", "cmbTransmission", "cmbFuelType", "txtMileage", "txtColor", "txtPlate",
        "txtDailyPrice", "txtImageUrl", "chkListed", "btnAddCar", "btnUpdateCar", "btnDeleteCar", "btnCalendar", "btnMaintenance",
        "btnRentals", "btnUsers", "btnRevenue", "btnSignOut",
    })]
    [InlineData(nameof(UserManagementPanel), new[] { "btnNewUser", "lstUsers", "txtUserName", "cmbRole", "txtPassword", "txtEmail", "txtPhone", "btnAddUser", "btnUpdateUser", "btnDeleteUser", "btnClose" })]
    [InlineData(nameof(MaintenanceForm), new[] { "lstRecords", "cmbKind", "txtCost", "dtpStart", "dtpEnd", "txtDescription", "btnNew", "btnSave", "btnClose" })]
    public void TabOrder_FollowsTheReadingOrder(string name, string[] expected) => Run(() =>
    {
        using var form = TestForms.Create(name);
        LayoutAssert.CreateHandles(form);

        var names = TabOrder(form).Select(control => control.Name).Where(expected.Contains).ToList();

        Assert.Equal(expected, names);
    });

    [Fact]
    public void FieldCaptions_NameTheirInputs() => Run(() =>
    {
        using var form = TestForms.Create(nameof(LoginForm));

        Assert.Equal("Kullanıcı adı", Find<TextBox>(form, "txtUserName").AccessibleName);
        Assert.Equal("Şifre", Find<TextBox>(form, "txtPassword").AccessibleName);
        Assert.False(Descendants(form).OfType<TextField>().Any(field => field.CanSelect), "A text field frame must hand the focus to its text box.");
    });

    /// <summary>A warning of a screen appears in its message strip, which screen readers announce, and no window opens.</summary>
    [Fact]
    public void Validation_IsShownInlineWithoutADialog() => Run(() =>
    {
        using var form = new UserManagementPanel(new ListedAccounts(), Sessions.Admin);
        var dialogs = NoDialogs.Requested.Count;

        Assert.False(form.UpdateAsync().Result);

        Assert.True(form.StatusBar.IsShown);
        Assert.Equal(StatusKind.Warning, form.StatusBar.Kind);
        Assert.Equal("Lütfen güncellemek için bir kullanıcı seçin.", form.StatusBar.Text);
        Assert.Equal(form.StatusBar.Text, form.StatusBar.AccessibleName);
        Assert.Equal(dialogs, NoDialogs.Requested.Count);
    });

    /// <summary>A write that starts clears the previous message, so an old warning never sits beside a new result.</summary>
    [Fact]
    public void NextWrite_ClearsThePreviousMessage() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new LoginForm(auth, new NoFormsFactory());
        var signIn = form.SignInAsync();
        auth.RefuseSignIn();
        Assert.True(signIn.IsCompletedSuccessfully);
        Assert.True(form.StatusBar.IsShown);

        _ = form.SignInAsync(); // Pending; the old warning must be gone meanwhile.

        Assert.False(form.StatusBar.IsShown);
    });

    /// <summary>Questions still go through the themed message window (here answered "No" by the test fixture).</summary>
    [Fact]
    public void CancelQuestion_GoesThroughTheDialogHelper() => Run(() =>
    {
        var rentals = new ControlledRentalService();
        using var form = new ProfileForm(rentals, new ControlledContractService(), new NoFormsFactory(), Sessions.User);
        Assert.True(form.LoadRentalsAsync().IsCompletedSuccessfully);
        var before = NoDialogs.Requested.Count;

        Assert.True(form.EndAsync(form.ShownRentals[0]).IsCompletedSuccessfully);

        Assert.Equal("Onay: " + RentalCards.Question(form.ShownRentals[0], underway: false), Assert.Single(NoDialogs.Requested.Skip(before)));
        Assert.Equal(0, rentals.EndCalls);
    });

    [Fact]
    public void MessageDialog_AnswersWithKeysAndIsNeverShown() => Run(() =>
    {
        using var dialog = new MessageDialog(RentalCards.Question(ControlledRentalService.Rental(9), underway: false), "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        Assert.False(dialog.Visible);
        Assert.Equal(DialogResult.Yes, Assert.IsType<ThemedButton>(dialog.AcceptButton).DialogResult);
        Assert.Equal(DialogResult.No, Assert.IsType<ThemedButton>(dialog.CancelButton).DialogResult);
        Assert.Equal(AppText.Title, dialog.Text);
        Assert.True(dialog.ClientSize.Width > 0 && dialog.ClientSize.Height > 0);
        Assert.Equal("Bir sorun oluştu", MessageDialog.Heading(AppText.Title, MessageBoxIcon.Error));
        Assert.Equal("Uyarı", MessageDialog.Heading("Uyarı", MessageBoxIcon.Warning));
    });

    /// <summary>The sign-in screen says why the previous session ended or what the step before it did.</summary>
    [Fact]
    public void SignIn_ShowsTheNoticeOfTheEndedSession() => Run(() =>
    {
        using var form = new LoginForm(new PendingAuthService(), new NoFormsFactory());

        form.ShowNotice(UserManagementPanel.OwnSessionEndedMessage);
        Assert.Equal((StatusKind.Info, UserManagementPanel.OwnSessionEndedMessage), (form.StatusBar.Kind, form.StatusBar.Text));

        form.ShowNotice(LoginForm.RegisteredMessage, StatusKind.Success);
        Assert.Equal((StatusKind.Success, LoginForm.RegisteredMessage), (form.StatusBar.Kind, form.StatusBar.Text));
    });

    /// <summary>The setup offered from the sign-in hides its key field while no key is asked for.</summary>
    [Fact]
    public void Setup_ShowsTheKeyFieldOnlyWhenAKeyIsAsked() => Run(() =>
    {
        using var form = new FirstAdminSetupForm(new PendingSetupService());
        var key = Find<TextBox>(form, "txtKey");

        Assert.True(FormLayout.IsHidden(key));
        form.RequireSetupKey = true;
        Assert.False(FormLayout.IsHidden(key));
        Assert.Equal(FirstAdminSetupForm.SetupKeyHint, Find<Label>(form, "lblDescription").Text);
    });

    internal static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Focusable controls in the order Tab visits them.</summary>
    private static List<Control> TabOrder(Form form)
    {
        var order = new List<Control>();
        Control? current = null;
        while ((current = form.GetNextControl(current, forward: true)) is not null)
        {
            if (CanSelectWhenShown(current))
            {
                order.Add(current);
            }
        }

        return order;
    }

    /// <summary>
    /// Whether Tab can stop on the control once the window is shown: an enabled tab stop that is not a
    /// container, label, frame or badge and not hidden by its screen.
    /// </summary>
    private static bool CanSelectWhenShown(Control control) =>
        control.TabStop
        && control.Enabled
        && !FormLayout.IsHidden(control)
        && control is not (Label or Panel or TableLayoutPanel or FlowLayoutPanel or TextField or DateField or GlyphBadge or InfoBar or BrandRule or PlateChip or StatusPill or VehiclePicture or DaySwatch);
}
