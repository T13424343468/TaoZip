// ShellExtStubs.cs - WinRT 部署类型桩: 仅用于语法/类型检查, 不随产品分发。
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Windows.Management.Deployment
{
    internal class PackageManager
    {
        public PackageManager() { }
        public IEnumerable<global::Windows.ApplicationModel.Package> FindPackages() =>
            new List<global::Windows.ApplicationModel.Package>();
        public Task<DeploymentResult> AddPackageByUriAsync(Uri uri, AddPackageOptions options) =>
            Task.FromResult(new DeploymentResult());
        public Task RemovePackageAsync(string fullName) => Task.CompletedTask;
    }

    internal class AddPackageOptions
    {
        public Uri? ExternalLocationUri { get; set; }
        public bool AllowUnsigned { get; set; }
    }

    internal class DeploymentResult
    {
        public bool IsRegistered { get; set; }
        public string ErrorText { get; set; } = "";
    }
}

namespace Windows.ApplicationModel
{
    internal class Package
    {
        public PackageId Id { get; } = new();
    }

    internal class PackageId
    {
        public string Name { get; set; } = "";
        public string FullName { get; set; } = "";
    }
}
