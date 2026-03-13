namespace Game.Server.Event;

public static class PasswordValidationEventManager
{
    public static bool ValidatePassword(string inputPassword, string requiredPassword, bool useExternalPassword, string nickname, string communityAccountId, string ip, object netNode, object client, out string customErrorMessage)
    {
        customErrorMessage = null;
        return true;
    }
}
