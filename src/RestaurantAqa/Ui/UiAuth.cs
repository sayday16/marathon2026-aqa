using System.Text.Json;
using Microsoft.Playwright;
using RestaurantAqa.Data;

namespace RestaurantAqa.Ui;

/// <summary>
/// Puts a user that is already signed in through the API into the browser, the way the application itself stores it
/// (<c>auth</c> and <c>user</c> in localStorage). Used by the scenarios where signing in is only a precondition;
/// the real sign-in form is covered separately.
/// </summary>
public static class UiAuth
{
    public static Task SignInAsync(IBrowserContext context, TestUser user)
    {
        string tokens = JsonSerializer.Serialize(new { idToken = user.Token, accessToken = user.Token });
        string profile = JsonSerializer.Serialize(new { id = user.Id, firstName = user.FirstName, lastName = user.LastName, email = user.Email, role = user.Role });

        string script = $"localStorage.setItem('auth', {JsonSerializer.Serialize(tokens)}); localStorage.setItem('user', {JsonSerializer.Serialize(profile)});";
        return context.AddInitScriptAsync(script);
    }
}
