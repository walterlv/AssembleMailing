﻿namespace Walterlv.AssembleMailing.Utils
{
    /// <summary>
    /// Provide methods to get or set user's password.<para/>
    /// We should not store the user's password directly, but we can use platform-specified method to store them.
    /// So there must be a password manager interface so that different platform can have it's own security solution.
    /// </summary>
    public interface IPasswordManager
    {
        /// <summary>
        /// Retrieve a user's password by a key. The key is commonly the users account id of mail address.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        string Retrieve(string key);

        /// <summary>
        /// Add to store a new password in a secure method. The key is commonly the users account id of mail address.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="password"></param>
        void Add(string key, string password);
    }
}