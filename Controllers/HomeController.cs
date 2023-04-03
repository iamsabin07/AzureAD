using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class HomeController : ControllerBase
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly IConfiguration _configuration;

        public HomeController(GraphServiceClient graphServiceClient, ITokenAcquisition tokenAcquisition, IConfiguration configuration)
        {
            _graphServiceClient = graphServiceClient;
            _tokenAcquisition = tokenAcquisition;
            _configuration = configuration;
        }
        [HttpPost]

        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            string clientId = _configuration["AzureAd:ClientId"];
            string clientSecret = _configuration["AzureAd:ClientSecret"];
            string tenantId = _configuration["AzureAd:TenantId"];
            string scope = "https://graph.microsoft.com/.default";

            var app = ConfidentialClientApplicationBuilder.Create(clientId)
                    .WithClientSecret(clientSecret)
                    .WithAuthority(new Uri($"https://login.microsoftonline.com/{tenantId}"))
                    .Build();

            string accessToken = await _tokenAcquisition.GetAccessTokenForAppAsync(scope);

            _graphServiceClient.AuthenticationProvider = new DelegateAuthenticationProvider(async (requestMessage) =>
            {
                // Set the Authorization header to include the access token
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            });
            var user = new User
            {
                AccountEnabled = true,
                DisplayName = $"{request.FirstName} {request.LastName}",
                MailNickname = request.FirstName.ToLowerInvariant() + "." + request.LastName.ToLowerInvariant(),
                UserPrincipalName = "AzureADWebApiSabin.onmicrosoft.com",
                PasswordProfile = new PasswordProfile
                {
                    Password = request.Password,
                    ForceChangePasswordNextSignIn = true
                },
                GivenName = request.FirstName,
                Surname = request.LastName,
            };

            try
            {
                var users = await _graphServiceClient.Users.Request().GetAsync();

                var createdUser = await _graphServiceClient.Users.Request().AddAsync(user);

                return Ok(createdUser);
            }
            catch (ServiceException ex)
            {            
                return BadRequest(ex.Error.Message);
            }
        }
    }

    public class CreateUserRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }
}