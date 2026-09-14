namespace BlazorApp1.Services
{
    public sealed class SidebarMenuState
    {
        public bool ReportsExpanded { get; set; }
        public bool HrExpanded { get; set; }
    }

    public interface ISessionService
    {
        SidebarMenuState SidebarMenus { get; }
        event Action? OnChange;
        bool IsAuthenticated { get; }
        string? CurrentUsername { get; }
        int? SelectedPeriodId { get; }
        string? SelectedPeriodName { get; }
        string? SelectedCompanyName { get; }
        string? SelectedDatabaseKey { get; }
        string? SelectedDatabaseName { get; }
        Task SelectDatabaseAsync(string databaseKey, string databaseName);
        Task LoginAsync(string username, int? selectedPeriodId = null, string? selectedPeriodName = null, string? selectedCompanyName = null);
        Task LogoutAsync();
    }

    public class SessionService : ISessionService
    {
        public SidebarMenuState SidebarMenus { get; } = new();
        private bool _isAuthenticated = false;
        private string? _currentUsername = null;
        private int? _selectedPeriodId;
        private string? _selectedPeriodName;
        private string? _selectedCompanyName;
        private string? _selectedDatabaseKey;
        private string? _selectedDatabaseName;

        public event Action? OnChange;

        public bool IsAuthenticated => _isAuthenticated;
        public string? CurrentUsername => _currentUsername;
        public int? SelectedPeriodId => _selectedPeriodId;
        public string? SelectedPeriodName => _selectedPeriodName;
        public string? SelectedCompanyName => _selectedCompanyName;
        public string? SelectedDatabaseKey => _selectedDatabaseKey;
        public string? SelectedDatabaseName => _selectedDatabaseName;

        public Task SelectDatabaseAsync(string databaseKey, string databaseName)
        {
            _isAuthenticated = false;
            _currentUsername = null;
            _selectedPeriodId = null;
            _selectedPeriodName = null;
            _selectedCompanyName = null;
            _selectedDatabaseKey = databaseKey;
            _selectedDatabaseName = databaseName;
            NotifyStateChanged();
            return Task.CompletedTask;
        }

        public Task LoginAsync(string username, int? selectedPeriodId = null, string? selectedPeriodName = null, string? selectedCompanyName = null)
        {
            if (string.IsNullOrWhiteSpace(_selectedDatabaseKey))
            {
                throw new InvalidOperationException("يجب اختيار المنشأة قبل تسجيل الدخول.");
            }

            _isAuthenticated = true;
            _currentUsername = username;
            _selectedPeriodId = selectedPeriodId;
            _selectedPeriodName = selectedPeriodName;
            _selectedCompanyName = selectedCompanyName;
            NotifyStateChanged();
            return Task.CompletedTask;
        }

        public Task LogoutAsync()
        {
            _isAuthenticated = false;
            _currentUsername = null;
            _selectedPeriodId = null;
            _selectedPeriodName = null;
            _selectedCompanyName = null;
            NotifyStateChanged();
            return Task.CompletedTask;
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}
