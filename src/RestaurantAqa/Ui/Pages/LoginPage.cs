using Microsoft.Playwright;

namespace RestaurantAqa.Ui.Pages;

public sealed class LoginPage(IPage page)
{
    public async Task OpenAsync() => await page.GotoAsync("/login");

    public ILocator Email => page.GetByPlaceholder("e.g. username@domain.com");

    // The password field has no placeholder or a unique accessible name (the label also contains the "Forgot password?" link).
    public ILocator Password => page.Locator("input[formcontrolname='password']");

    public ILocator SignInButton => page.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true });

    public ILocator IncorrectCredentialsError => page.GetByText("Incorrect email or password");

    public async Task SignInAsync(string email, string password)
    {
        await Email.FillAsync(email);
        await Password.FillAsync(password);
        await SignInButton.ClickAsync();
    }

    /// <summary>The application keeps the session in localStorage; its presence means the user is signed in.</summary>
    public async Task<bool> HasSessionAsync() =>
        await page.EvaluateAsync<bool>("() => localStorage.getItem('auth') !== null");
}
