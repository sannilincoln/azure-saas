using Dawn;
using Saas.Admin.Client;
using Saas.SignupAdministration.Web.Services.StateMachine;


namespace Saas.SignupAdministration.Web.Services;

public class OnboardingWorkflowService
{
    private readonly IOnboardingAdminClient _onboardingClient;
    private readonly IPersistenceProvider _persistenceProvider;
    private readonly IApplicationUser _applicationUser;
    private readonly IEmail _email;

    public OnboardingWorkflowItem OnboardingWorkflowItem { get; internal set; }
    public OnboardingWorkflowState OnboardingWorkflowState { get; internal set; }

    public OnboardingWorkflowState.States CurrentState
    {
        get
        {
            return OnboardingWorkflowState.CurrentState;
        }
    }

    public OnboardingWorkflowService(IApplicationUser applicationUser, IOnboardingAdminClient onboardingClient, IPersistenceProvider persistenceProvider, IEmail email)
    {
        _applicationUser = applicationUser;
        _onboardingClient = onboardingClient;
        _persistenceProvider = persistenceProvider;
        _email = email;

        OnboardingWorkflowItem? item = _persistenceProvider.Retrieve<OnboardingWorkflowItem>(SR.OnboardingWorkflowItemKey);
        OnboardingWorkflowState? state = _persistenceProvider.Retrieve<OnboardingWorkflowState>(SR.OnboardingWorkflowStateKey);

        OnboardingWorkflowItem = (item is null) ? new(Guard.Argument(applicationUser?.NameIdentifier).NotNull().NotDefault().ToString()) : item;
        OnboardingWorkflowState = (state is null) ? new() : state;
    }

    public void TransitionState(OnboardingWorkflowState.Triggers trigger)
    {
        OnboardingWorkflowState.CurrentState = OnboardingWorkflowState.Transition(trigger);
    }

    public async Task OnboardTenant()
    {
        // Guard against a lost/expired session producing a blank tenant (which then collides
        // on the unique route index and surfaces as an opaque 500 from the Admin API).
        if (string.IsNullOrWhiteSpace(OnboardingWorkflowItem.OrganizationName)
            || string.IsNullOrWhiteSpace(OnboardingWorkflowItem.TenantRouteName))
        {
            throw new InvalidOperationException(
                "Onboarding data was lost before submission (organization name / route are empty). " +
                "This usually means the session expired or the web app was restarted mid-wizard. " +
                "Please restart the onboarding from the beginning.");
        }

        OnboardingTenantRequest tenantRequest = new(
            Name: OnboardingWorkflowItem.OrganizationName,
            Route: OnboardingWorkflowItem.TenantRouteName,
            CreatorEmail: _applicationUser.EmailAddress,
            // The signed-in customer becomes the admin of the new tenant. Passed explicitly because the
            // Admin API call is app-only (no user token) — see OnboardingAdminClient.
            CreatorObjectId: _applicationUser.NameIdentifier,
            ProductTierId: OnboardingWorkflowItem.ProductId,
            CategoryId: OnboardingWorkflowItem.CategoryId);

        // Call the Admin API app-only (service-to-service).
        Guid tenantId = await _onboardingClient.CreateTenantAsync(tenantRequest);

        // Marketplace-originated onboarding: now that the tenant exists, activate the
        // subscription (this starts billing) and link it to the tenant. Activation happens
        // only after provisioning succeeds, so we never bill for a tenant that failed to create.
        if (OnboardingWorkflowItem.SubscriptionId is Guid subscriptionId)
        {
            await _onboardingClient.ActivateAsync(subscriptionId, tenantId);
        }

        OnboardingWorkflowItem.IsComplete = true;
        OnboardingWorkflowItem.Created = DateTime.Now;

        await _email.SendAsync(_applicationUser.EmailAddress);
    }

    public void PersistToSession()
    {
        _persistenceProvider.Persist(SR.OnboardingWorkflowStateKey, OnboardingWorkflowState);
        _persistenceProvider.Persist(SR.OnboardingWorkflowItemKey, OnboardingWorkflowItem);
    }

    public async Task<bool> GetRouteExistsAsync(string route)
    {
        return !await _onboardingClient.IsValidPathAsync(route);
    }
}
