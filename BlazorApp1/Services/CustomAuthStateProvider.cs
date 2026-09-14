using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace BlazorApp1.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider, IDisposable
    {
        private readonly ISessionService _sessionService;
        private ClaimsPrincipal _anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        public CustomAuthStateProvider(ISessionService sessionService)
        {
            _sessionService = sessionService;
            _sessionService.OnChange += StateHasChanged;
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            if (_sessionService.IsAuthenticated && !string.IsNullOrEmpty(_sessionService.CurrentUsername))
            {
                var identity = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, _sessionService.CurrentUsername),
                    new Claim(ClaimTypes.Role, "User")
                }, "CustomAuth");

                var user = new ClaimsPrincipal(identity);
                return Task.FromResult(new AuthenticationState(user));
            }

            return Task.FromResult(new AuthenticationState(_anonymous));
        }

        public void Dispose() => _sessionService.OnChange -= StateHasChanged;

        public void StateHasChanged()
        {
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
    }
}
