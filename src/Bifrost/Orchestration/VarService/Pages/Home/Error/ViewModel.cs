using Duende.IdentityServer.Models;

namespace VarService.Pages.Error
{
    public class ViewModel
    {
        public ViewModel()
        {
        }

        public ViewModel(string error) => Error = new ErrorMessage { Error = error };

        public ErrorMessage? Error { get; set; }
    }
}
