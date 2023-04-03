using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UserController : ControllerBase
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly IConfiguration _configuration;

        public UserController(GraphServiceClient graphServiceClient, ITokenAcquisition tokenAcquisition, IConfiguration configuration)
        {
            _graphServiceClient = graphServiceClient;
            _tokenAcquisition = tokenAcquisition;
            _configuration = configuration;
        }

        [Authorize]
        [HttpGet("login")]
        public IActionResult Login()
        {
            // This will redirect the user to the Microsoft login page
            // After successful authentication, the user will be redirected back to this controller
            // with an authorization code that can be used to retrieve an access token
            return Challenge(new AuthenticationProperties { RedirectUri = Url.Action(nameof(CreateUser)) });
        }

        [HttpGet("add")]
        public async Task<IActionResult> AddExternalUser(string email)
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

            var invitation = new Invitation
            {
                InvitedUserEmailAddress = email,
                InviteRedirectUrl = "https://localhost:7298/signin-oidc",
                SendInvitationMessage = true,
                InvitedUserType = "Member"
            };

            var result = await _graphServiceClient.Invitations
                .Request()
                .AddAsync(invitation);

            var invitedUserId = result.InvitedUser.Id;
            var groupName = "ADSWORKS";

            var groups = await _graphServiceClient.Groups
                .Request()
                .Filter($"displayName eq '{groupName}'")
                .GetAsync();

            var groupObjectId = groups[0].Id;

            var group = await _graphServiceClient.Groups[groupObjectId].Request().GetAsync();

            await _graphServiceClient.Groups[group.Id].Members.References.Request().AddAsync(new DirectoryObject
            {
                Id = invitedUserId
            });

            return Ok(result);
        }

        [HttpGet("add-group")]
        public async Task<IActionResult> AddExternalGroup(string name)
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

            var newGroup = new Group
            {
                DisplayName = "ADSWORKS",
                MailNickname = "adsgroup",
                MailEnabled = false,
                SecurityEnabled = true
            };
            var createdGroup = await _graphServiceClient.Groups
                .Request()
                .AddAsync(newGroup);

            // Get the Admin security group
            var adminGroup = await _graphServiceClient.Groups["b5f6d83e-1249-4a73-aa9a-3b1639ebb4c0"].Request().GetAsync();

            // Add the new group as a member of the Admin security group
            await _graphServiceClient.Groups[adminGroup.Id].Members.References.Request().AddAsync(new DirectoryObject
            {
                Id = createdGroup.Id
            });

            return Ok(createdGroup);
        }


        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateUser()
        {
            // Get the user's email address from the claims
            string userEmail = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name").Value;

            // Retrieve an access token for the Graph API
            string[] scopes = new[] { "https://graph.microsoft.com/.default" };
            string accessToken = await _tokenAcquisition.GetAccessTokenForUserAsync(scopes);

            // Set the authentication provider for the Graph API client to use the access token
            _graphServiceClient.AuthenticationProvider = new DelegateAuthenticationProvider(async (requestMessage) =>
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            });

            // Create a new user using the user's information from the claims
            var user = new User
            {
                AccountEnabled = true,
                DisplayName = $"{User.Identity.Name}",
                MailNickname = userEmail,
                UserPrincipalName = userEmail,
                PasswordProfile = new PasswordProfile
                {
                    Password = Guid.NewGuid().ToString(), // set a random password
                    ForceChangePasswordNextSignIn = true
                },
                GivenName = User.FindFirst("given_name").Value,
                Surname = User.FindFirst("family_name").Value,
            };

            try
            {
                // Add the new user to Azure AD using the Graph API
                var createdUser = await _graphServiceClient.Users.Request().AddAsync(user);

                return Ok(createdUser);
            }
            catch (ServiceException ex)
            {
                return BadRequest(ex.Error.Message);
            }
        }
    }
}