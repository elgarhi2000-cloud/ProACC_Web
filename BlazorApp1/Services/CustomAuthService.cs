using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace BlazorApp1.Services
{
    public interface ICustomAuthService
    {
        Task<bool> ValidateUserAsync(string username, string password);
    }

    public class CustomAuthService : ICustomAuthService
    {
        private readonly ICompanyDatabaseService _companyDatabaseService;
        private readonly ISessionService _sessionService;

        public CustomAuthService(
            ICompanyDatabaseService companyDatabaseService,
            ISessionService sessionService)
        {
            _companyDatabaseService = companyDatabaseService;
            _sessionService = sessionService;
        }

    public async Task<bool> ValidateUserAsync(string username, string password)
    {
        try
        {
            var trimmedUsername = (username ?? string.Empty).Trim();
            var trimmedPassword = (password ?? string.Empty);

            if (string.IsNullOrWhiteSpace(trimmedUsername) || string.IsNullOrEmpty(trimmedPassword))
            {
                return false;
            }

            if (trimmedUsername.Length > 100 || trimmedPassword.Length > 1024) return false;
            var attemptKey = $"company:{_sessionService.SelectedDatabaseKey}:{trimmedUsername}";
            if (!LoginAttemptLimiter.TryBegin(attemptKey)) return false;
            var connectionString = _companyDatabaseService.BuildConnectionString(_sessionService.SelectedDatabaseKey);
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            using var command = new SqlCommand("SELECT UserPass FROM [USER] WHERE UserName = @UserName", connection);
            command.Parameters.AddWithValue("@UserName", trimmedUsername);
            var result = await command.ExecuteScalarAsync();
            var valid = result is string stored && LegacyPasswordVerifier.Verify(stored, trimmedPassword);
            if (valid) LoginAttemptLimiter.Reset(attemptKey);
            return valid;
        }
            catch (Exception ex)
            {
                // ظٹظ…ظƒظ†ظƒ ط¥ط¶ط§ظپط© logging ظ‡ظ†ط§
                Console.WriteLine($"Error validating user: {ex.Message}");
                return false;
            }
        }
    }
}
