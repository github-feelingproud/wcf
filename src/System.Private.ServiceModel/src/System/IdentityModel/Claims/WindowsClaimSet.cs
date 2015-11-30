// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IdentityModel.Policy;
using System.Runtime;
using System.Security;
using System.Security.Principal;
using System.ServiceModel;

namespace System.IdentityModel.Claims
{
    public class WindowsClaimSet : ClaimSet, IIdentityInfo, IDisposable
    {
        internal const bool DefaultIncludeWindowsGroups = true;
        private WindowsIdentity _windowsIdentity;
        private DateTime _expirationTime;
        private bool _includeWindowsGroups;
        private IList<Claim> _claims;
        // Not sure yet if GroupSidClaimCollections are necessary in .NET Core
        //private GroupSidClaimCollection _groups;
        private bool _disposed = false;
        private string _authenticationType;

        public WindowsClaimSet(WindowsIdentity windowsIdentity)
            : this(windowsIdentity, DefaultIncludeWindowsGroups)
        {
        }

        public WindowsClaimSet(WindowsIdentity windowsIdentity, bool includeWindowsGroups)
            : this(windowsIdentity, includeWindowsGroups, DateTime.UtcNow.AddHours(10))
        {
        }

        public WindowsClaimSet(WindowsIdentity windowsIdentity, DateTime expirationTime)
            : this(windowsIdentity, DefaultIncludeWindowsGroups, expirationTime)
        {
        }

        public WindowsClaimSet(WindowsIdentity windowsIdentity, bool includeWindowsGroups, DateTime expirationTime)
            : this(windowsIdentity, null, includeWindowsGroups, expirationTime, true)
        {
        }

        public WindowsClaimSet(WindowsIdentity windowsIdentity, string authenticationType, bool includeWindowsGroups, DateTime expirationTime)
            : this( windowsIdentity, authenticationType, includeWindowsGroups, expirationTime, true )
        {
        }

        internal WindowsClaimSet(WindowsIdentity windowsIdentity, string authenticationType, bool includeWindowsGroups, bool clone)
            : this( windowsIdentity, authenticationType, includeWindowsGroups, DateTime.UtcNow.AddHours( 10 ), clone )
        {
        }

        internal WindowsClaimSet(WindowsIdentity windowsIdentity, string authenticationType, bool includeWindowsGroups, DateTime expirationTime, bool clone)
        {
            if (windowsIdentity == null)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("windowsIdentity");

            _windowsIdentity = clone ? SecurityUtils.CloneWindowsIdentityIfNecessary(windowsIdentity,  authenticationType) : windowsIdentity;
            _includeWindowsGroups = includeWindowsGroups;
            _expirationTime = expirationTime;
            _authenticationType = authenticationType;
        }

        private WindowsClaimSet(WindowsClaimSet from)
            : this(from.WindowsIdentity, from._authenticationType, from._includeWindowsGroups, from._expirationTime, true)
        {
        }

        public override Claim this[int index]
        {
            get
            {
                ThrowIfDisposed();
                EnsureClaims();
                return _claims[index];
            }
        }

        public override int Count
        {
            get
            {
                ThrowIfDisposed();
                EnsureClaims();
                return _claims.Count;
            }
        }

        IIdentity IIdentityInfo.Identity
        {
            get
            {
                ThrowIfDisposed();
                return _windowsIdentity;
            }
        }

        public WindowsIdentity WindowsIdentity
        {
            get
            {
                ThrowIfDisposed();
                return _windowsIdentity;
            }
        }
       
        public override ClaimSet Issuer
        {
            get { return ClaimSet.Windows; }
        }

        public DateTime ExpirationTime
        {
            get { return _expirationTime; }
        }

        // Not sure yet if GroupSidClaimCollections are necessary in .NET Core
        //internal GroupSidClaimCollection Groups
        //{
        //    get
        //    {
        //        if (_groups == null)
        //        {
        //            _groups = new GroupSidClaimCollection(_windowsIdentity);
        //        }
        //        return _groups;
        //    }
        //}

        internal WindowsClaimSet Clone()
        {
            ThrowIfDisposed();
            return new WindowsClaimSet(this);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _windowsIdentity.Dispose();
            }
        }

        IList<Claim> InitializeClaimsCore()
        {
            if (_windowsIdentity.AccessToken == null)
                return new List<Claim>();

            List<Claim> claims = new List<Claim>(3);
            claims.Add(new Claim(ClaimTypes.Sid, _windowsIdentity.User, Rights.Identity));
            Claim claim;
            if (TryCreateWindowsSidClaim(_windowsIdentity, out claim))
            {
                claims.Add(claim);
            }
            claims.Add(Claim.CreateNameClaim(_windowsIdentity.Name));
            if (_includeWindowsGroups)
            {
                // claims.AddRange(Groups);
            }
            return claims;
        }

        void EnsureClaims()
        {
            if (_claims != null)
                return;

            _claims = InitializeClaimsCore();
        }

        void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new ObjectDisposedException(this.GetType().FullName));
            }
        }

        static bool SupportedClaimType(string claimType)
        {
            return claimType == null ||
                ClaimTypes.Sid == claimType ||
                ClaimTypes.DenyOnlySid == claimType ||
                ClaimTypes.Name == claimType;
        }

        // Note: null string represents any.
        public override IEnumerable<Claim> FindClaims(string claimType, string right)
        {
            ThrowIfDisposed();
            if (!SupportedClaimType(claimType) || !ClaimSet.SupportedRight(right))
            {
                yield break;
            }
            else if (_claims == null && (ClaimTypes.Sid == claimType || ClaimTypes.DenyOnlySid == claimType))
            {
                if (ClaimTypes.Sid == claimType)
                {
                    if (right == null || Rights.Identity == right)
                    {
                        yield return new Claim(ClaimTypes.Sid, _windowsIdentity.User, Rights.Identity);
                    }
                }

                if (right == null || Rights.PossessProperty == right)
                {
                    Claim sid;
                    if (TryCreateWindowsSidClaim(_windowsIdentity, out sid))
                    {
                        if (claimType == sid.ClaimType)
                        {
                            yield return sid;
                        }
                    }
                }

                if (_includeWindowsGroups && (right == null || Rights.PossessProperty == right))
                {
                    // Not sure yet if GroupSidClaimCollections are necessary in .NET Core
                    //for (int i = 0; i < Groups.Count; ++i)
                    //{
                    //    Claim sid = Groups[i];
                    //    if (claimType == sid.ClaimType)
                    //    {
                    //        yield return sid;
                    //    }
                    //}
                }
            }
            else
            {
                EnsureClaims();

                bool anyClaimType = (claimType == null);
                bool anyRight = (right == null);

                for (int i = 0; i < _claims.Count; ++i)
                {
                    Claim claim = _claims[i];
                    if ((claim != null) &&
                        (anyClaimType || claimType == claim.ClaimType) &&
                        (anyRight || right == claim.Right))
                    {
                        yield return claim;
                    }
                }
            }
        }

        public override IEnumerator<Claim> GetEnumerator()
        {
            ThrowIfDisposed();
            EnsureClaims();
            return _claims.GetEnumerator();
        }

        public override string ToString()
        {
            return _disposed ? base.ToString() : SecurityUtils.ClaimSetToString(this);
        }

        [Fx.Tag.SecurityNote(Critical = "Uses critical type SafeHGlobalHandle.",
            Safe = "Performs a Demand for full trust.")]
        [SecuritySafeCritical]
        public static bool TryCreateWindowsSidClaim(WindowsIdentity windowsIdentity, out Claim claim)
        {
            throw ExceptionHelper.PlatformNotSupported();

            // Not sure yet if GroupSidClaimCollections are necessary in .NET Core
            //SafeHGlobalHandle safeAllocHandle = SafeHGlobalHandle.InvalidHandle;
            //try
            //{
            //    uint dwLength;
            //    safeAllocHandle = GetTokenInformation(windowsIdentity.Token, TokenInformationClass.TokenUser, out dwLength);
            //    SID_AND_ATTRIBUTES user = (SID_AND_ATTRIBUTES)Marshal.PtrToStructure(safeAllocHandle.DangerousGetHandle(), typeof(SID_AND_ATTRIBUTES));
            //    uint mask = NativeMethods.SE_GROUP_USE_FOR_DENY_ONLY;
            //    if (user.Attributes == 0)
            //    {
            //        claim = Claim.CreateWindowsSidClaim(new SecurityIdentifier(user.Sid));
            //        return true;
            //    }
            //    else if ((user.Attributes & mask) == NativeMethods.SE_GROUP_USE_FOR_DENY_ONLY)
            //    {
            //        claim = Claim.CreateDenyOnlyWindowsSidClaim(new SecurityIdentifier(user.Sid));
            //        return true;
            //    }
            //}
            //finally
            //{
            //    safeAllocHandle.Close();
            //}
            //claim = null;
            //return false;
        }

    }
}
