export namespace auth {
	
	export class AuthlibCredential {
	    Username: string;
	    ProfileName: string;
	    ProfileUuid: string;
	    AccessToken: string;
	    ApiRoot: string;
	    ServerName: string;
	
	    static createFrom(source: any = {}) {
	        return new AuthlibCredential(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Username = source["Username"];
	        this.ProfileName = source["ProfileName"];
	        this.ProfileUuid = source["ProfileUuid"];
	        this.AccessToken = source["AccessToken"];
	        this.ApiRoot = source["ApiRoot"];
	        this.ServerName = source["ServerName"];
	    }
	}
	export class AuthlibProfileInfo {
	    Id: string;
	    Name: string;
	
	    static createFrom(source: any = {}) {
	        return new AuthlibProfileInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Id = source["Id"];
	        this.Name = source["Name"];
	    }
	}
	export class AuthlibLoginResult {
	    AccessToken: string;
	    Profiles: AuthlibProfileInfo[];
	    ServerName: string;
	
	    static createFrom(source: any = {}) {
	        return new AuthlibLoginResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.AccessToken = source["AccessToken"];
	        this.Profiles = this.convertValues(source["Profiles"], AuthlibProfileInfo);
	        this.ServerName = source["ServerName"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	
	export class AuthlibServerInfo {
	    ApiRoot: string;
	    ServerName: string;
	    SkinDomains: string[];
	
	    static createFrom(source: any = {}) {
	        return new AuthlibServerInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.ApiRoot = source["ApiRoot"];
	        this.ServerName = source["ServerName"];
	        this.SkinDomains = source["SkinDomains"];
	    }
	}
	export class MicrosoftAccount {
	    Username: string;
	    Uuid: string;
	    AccessToken: string;
	    RefreshToken: string;
	    XboxUserId: string;
	    ClientId: string;
	    // Go type: time
	    ExpiresAt: any;
	
	    static createFrom(source: any = {}) {
	        return new MicrosoftAccount(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Username = source["Username"];
	        this.Uuid = source["Uuid"];
	        this.AccessToken = source["AccessToken"];
	        this.RefreshToken = source["RefreshToken"];
	        this.XboxUserId = source["XboxUserId"];
	        this.ClientId = source["ClientId"];
	        this.ExpiresAt = this.convertValues(source["ExpiresAt"], null);
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class LaunchAccount {
	    Type: string;
	    DisplayName: string;
	    OfflineName: string;
	    OfflineSkinId: string;
	    Microsoft?: MicrosoftAccount;
	    Authlib?: AuthlibCredential;
	
	    static createFrom(source: any = {}) {
	        return new LaunchAccount(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Type = source["Type"];
	        this.DisplayName = source["DisplayName"];
	        this.OfflineName = source["OfflineName"];
	        this.OfflineSkinId = source["OfflineSkinId"];
	        this.Microsoft = this.convertValues(source["Microsoft"], MicrosoftAccount);
	        this.Authlib = this.convertValues(source["Authlib"], AuthlibCredential);
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}

}

export namespace bindings {
	
	export class OfflineSkinChoice {
	    id: string;
	    displayName: string;
	    model: string;
	    fallbackText: string;
	    source: string;
	
	    static createFrom(source: any = {}) {
	        return new OfflineSkinChoice(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.displayName = source["displayName"];
	        this.model = source["model"];
	        this.fallbackText = source["fallbackText"];
	        this.source = source["source"];
	    }
	}

}

export namespace config {
	
	export class GameVersionProfile {
	    MinecraftDirectory: string;
	    VersionId: string;
	    MinimumMemoryMb: number;
	    MaximumMemoryMb: number;
	    UseIndependentMemorySettings: boolean;
	    FollowGlobalAdvancedSettings: boolean;
	    WindowWidth: number;
	    WindowHeight: number;
	    IsVersionIsolationEnabled?: boolean;
	    JavaExecutable: string;
	    AdditionalJvmArguments: string[];
	    AdditionalGameArguments: string[];
	    InstanceIconOverride?: string;
	
	    static createFrom(source: any = {}) {
	        return new GameVersionProfile(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.MinecraftDirectory = source["MinecraftDirectory"];
	        this.VersionId = source["VersionId"];
	        this.MinimumMemoryMb = source["MinimumMemoryMb"];
	        this.MaximumMemoryMb = source["MaximumMemoryMb"];
	        this.UseIndependentMemorySettings = source["UseIndependentMemorySettings"];
	        this.FollowGlobalAdvancedSettings = source["FollowGlobalAdvancedSettings"];
	        this.WindowWidth = source["WindowWidth"];
	        this.WindowHeight = source["WindowHeight"];
	        this.IsVersionIsolationEnabled = source["IsVersionIsolationEnabled"];
	        this.JavaExecutable = source["JavaExecutable"];
	        this.AdditionalJvmArguments = source["AdditionalJvmArguments"];
	        this.AdditionalGameArguments = source["AdditionalGameArguments"];
	        this.InstanceIconOverride = source["InstanceIconOverride"];
	    }
	}
	export class GlobalLaunchSettings {
	    WindowWidth: number;
	    WindowHeight: number;
	    JavaExecutable: string;
	    AdditionalJvmArguments: string[];
	    AdditionalGameArguments: string[];
	
	    static createFrom(source: any = {}) {
	        return new GlobalLaunchSettings(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.WindowWidth = source["WindowWidth"];
	        this.WindowHeight = source["WindowHeight"];
	        this.JavaExecutable = source["JavaExecutable"];
	        this.AdditionalJvmArguments = source["AdditionalJvmArguments"];
	        this.AdditionalGameArguments = source["AdditionalGameArguments"];
	    }
	}
	export class JavaPathItem {
	    JavaPath: string;
	    JavaVersion: string;
	
	    static createFrom(source: any = {}) {
	        return new JavaPathItem(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.JavaPath = source["JavaPath"];
	        this.JavaVersion = source["JavaVersion"];
	    }
	}

}

export namespace content {
	
	export class GameContentEntry {
	    Name: string;
	    MetadataLine: string;
	    Description: string;
	    IconPath: string;
	    FallbackGlyph: string;
	    SourcePath: string;
	    IsDisabled: boolean;
	
	    static createFrom(source: any = {}) {
	        return new GameContentEntry(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Name = source["Name"];
	        this.MetadataLine = source["MetadataLine"];
	        this.Description = source["Description"];
	        this.IconPath = source["IconPath"];
	        this.FallbackGlyph = source["FallbackGlyph"];
	        this.SourcePath = source["SourcePath"];
	        this.IsDisabled = source["IsDisabled"];
	    }
	}
	export class GameInstanceVisual {
	    IconPath: string;
	    FallbackGlyph: string;
	
	    static createFrom(source: any = {}) {
	        return new GameInstanceVisual(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.IconPath = source["IconPath"];
	        this.FallbackGlyph = source["FallbackGlyph"];
	    }
	}

}

export namespace download {
	
	export class DownloadSource {
	    Name: string;
	    LauncherMeta: string;
	    Meta: string;
	    Libraries: string;
	    Resources: string;
	    Maven: string;
	
	    static createFrom(source: any = {}) {
	        return new DownloadSource(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Name = source["Name"];
	        this.LauncherMeta = source["LauncherMeta"];
	        this.Meta = source["Meta"];
	        this.Libraries = source["Libraries"];
	        this.Resources = source["Resources"];
	        this.Maven = source["Maven"];
	    }
	}
	export class GameDownloadSnapshot {
	    Revision: number;
	    TaskID: number;
	    Phase: number;
	    VersionID: string;
	    StageIndex: number;
	    StageName: string;
	    Detail: string;
	    Percentage: number;
	    CompletedBytes: number;
	    TotalBytes: number;
	    CompletedFiles: number;
	    TotalFiles: number;
	    BytesPerSecond: number;
	
	    static createFrom(source: any = {}) {
	        return new GameDownloadSnapshot(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Revision = source["Revision"];
	        this.TaskID = source["TaskID"];
	        this.Phase = source["Phase"];
	        this.VersionID = source["VersionID"];
	        this.StageIndex = source["StageIndex"];
	        this.StageName = source["StageName"];
	        this.Detail = source["Detail"];
	        this.Percentage = source["Percentage"];
	        this.CompletedBytes = source["CompletedBytes"];
	        this.TotalBytes = source["TotalBytes"];
	        this.CompletedFiles = source["CompletedFiles"];
	        this.TotalFiles = source["TotalFiles"];
	        this.BytesPerSecond = source["BytesPerSecond"];
	    }
	}
	export class InstalledJavaRuntime {
	    DirectoryPath: string;
	    JavaExecutablePath: string;
	    MajorVersion?: number;
	    Vendor?: number;
	
	    static createFrom(source: any = {}) {
	        return new InstalledJavaRuntime(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.DirectoryPath = source["DirectoryPath"];
	        this.JavaExecutablePath = source["JavaExecutablePath"];
	        this.MajorVersion = source["MajorVersion"];
	        this.Vendor = source["Vendor"];
	    }
	}
	export class JavaDownloadCandidate {
	    Vendor: number;
	    MajorVersion: number;
	    BuildVersion: string;
	    DownloadURL: string;
	    SHA256: string;
	    SizeBytes: number;
	
	    static createFrom(source: any = {}) {
	        return new JavaDownloadCandidate(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Vendor = source["Vendor"];
	        this.MajorVersion = source["MajorVersion"];
	        this.BuildVersion = source["BuildVersion"];
	        this.DownloadURL = source["DownloadURL"];
	        this.SHA256 = source["SHA256"];
	        this.SizeBytes = source["SizeBytes"];
	    }
	}
	export class ModLoaderVersion {
	    Type: number;
	    LoaderVersion: string;
	    IsStable: boolean;
	    BuildNumber: number;
	    MetadataURL: string;
	    RequiresInstallerExtraction: boolean;
	
	    static createFrom(source: any = {}) {
	        return new ModLoaderVersion(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Type = source["Type"];
	        this.LoaderVersion = source["LoaderVersion"];
	        this.IsStable = source["IsStable"];
	        this.BuildNumber = source["BuildNumber"];
	        this.MetadataURL = source["MetadataURL"];
	        this.RequiresInstallerExtraction = source["RequiresInstallerExtraction"];
	    }
	}
	export class ModpackInstallResult {
	    InstalledFiles: number;
	    DownloadedMods: number;
	    Errors: string[];
	
	    static createFrom(source: any = {}) {
	        return new ModpackInstallResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.InstalledFiles = source["InstalledFiles"];
	        this.DownloadedMods = source["DownloadedMods"];
	        this.Errors = source["Errors"];
	    }
	}
	export class ModpackRequirements {
	    MinecraftVersion: string;
	    LoaderType: number;
	    LoaderVersion: string;
	    RawLoaderKey: string;
	
	    static createFrom(source: any = {}) {
	        return new ModpackRequirements(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.MinecraftVersion = source["MinecraftVersion"];
	        this.LoaderType = source["LoaderType"];
	        this.LoaderVersion = source["LoaderVersion"];
	        this.RawLoaderKey = source["RawLoaderKey"];
	    }
	}

}

export namespace instance {
	
	export class ExternalGameInstanceLayout {
	    InstanceId: string;
	    InstanceDirectory: string;
	    ContentDirectory: string;
	    LauncherRoot: string;
	    Provider: string;
	    Evidence: string;
	
	    static createFrom(source: any = {}) {
	        return new ExternalGameInstanceLayout(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.InstanceId = source["InstanceId"];
	        this.InstanceDirectory = source["InstanceDirectory"];
	        this.ContentDirectory = source["ContentDirectory"];
	        this.LauncherRoot = source["LauncherRoot"];
	        this.Provider = source["Provider"];
	        this.Evidence = source["Evidence"];
	    }
	}
	export class GameInstanceSnapshot {
	    SourcePath: string;
	    MinecraftDirectory: string;
	    GameDirectory: string;
	    VersionIds: string[];
	    SelectedVersionId: string;
	    UsesVersionDirectoryAsGameDirectory: boolean;
	    IsLoading: boolean;
	    ErrorMessage: string;
	
	    static createFrom(source: any = {}) {
	        return new GameInstanceSnapshot(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.SourcePath = source["SourcePath"];
	        this.MinecraftDirectory = source["MinecraftDirectory"];
	        this.GameDirectory = source["GameDirectory"];
	        this.VersionIds = source["VersionIds"];
	        this.SelectedVersionId = source["SelectedVersionId"];
	        this.UsesVersionDirectoryAsGameDirectory = source["UsesVersionDirectoryAsGameDirectory"];
	        this.IsLoading = source["IsLoading"];
	        this.ErrorMessage = source["ErrorMessage"];
	    }
	}
	export class GameVersionDetails {
	    VersionId: string;
	    VersionDirectory: string;
	    ContentDirectory: string;
	    LayoutProvider: string;
	    LayoutEvidence: string;
	    IsIsolated: boolean;
	    IsExternallyManaged: boolean;
	    VersionType: string;
	    BaseGameVersion: string;
	    LoaderName: string;
	    LoaderVersion: string;
	    InstanceIconPath: string;
	    InstanceIconGlyph: string;
	    ReleaseTime: string;
	    MainClass: string;
	    JavaRequirement: string;
	    Mods: content.GameContentEntry[];
	    ResourcePacks: content.GameContentEntry[];
	    Shaders: content.GameContentEntry[];
	    Saves: content.GameContentEntry[];
	    HasShaderDirectory: boolean;
	
	    static createFrom(source: any = {}) {
	        return new GameVersionDetails(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.VersionId = source["VersionId"];
	        this.VersionDirectory = source["VersionDirectory"];
	        this.ContentDirectory = source["ContentDirectory"];
	        this.LayoutProvider = source["LayoutProvider"];
	        this.LayoutEvidence = source["LayoutEvidence"];
	        this.IsIsolated = source["IsIsolated"];
	        this.IsExternallyManaged = source["IsExternallyManaged"];
	        this.VersionType = source["VersionType"];
	        this.BaseGameVersion = source["BaseGameVersion"];
	        this.LoaderName = source["LoaderName"];
	        this.LoaderVersion = source["LoaderVersion"];
	        this.InstanceIconPath = source["InstanceIconPath"];
	        this.InstanceIconGlyph = source["InstanceIconGlyph"];
	        this.ReleaseTime = source["ReleaseTime"];
	        this.MainClass = source["MainClass"];
	        this.JavaRequirement = source["JavaRequirement"];
	        this.Mods = this.convertValues(source["Mods"], content.GameContentEntry);
	        this.ResourcePacks = this.convertValues(source["ResourcePacks"], content.GameContentEntry);
	        this.Shaders = this.convertValues(source["Shaders"], content.GameContentEntry);
	        this.Saves = this.convertValues(source["Saves"], content.GameContentEntry);
	        this.HasShaderDirectory = source["HasShaderDirectory"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class GameVersionLayout {
	    IsIsolated: boolean;
	    ContentDirectory: string;
	    Provider: string;
	    Evidence: string;
	
	    static createFrom(source: any = {}) {
	        return new GameVersionLayout(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.IsIsolated = source["IsIsolated"];
	        this.ContentDirectory = source["ContentDirectory"];
	        this.Provider = source["Provider"];
	        this.Evidence = source["Evidence"];
	    }
	}
	export class MinecraftInstallationLocation {
	    MinecraftDirectory: string;
	    PreferredVersionId: string;
	    GameDirectory: string;
	
	    static createFrom(source: any = {}) {
	        return new MinecraftInstallationLocation(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.MinecraftDirectory = source["MinecraftDirectory"];
	        this.PreferredVersionId = source["PreferredVersionId"];
	        this.GameDirectory = source["GameDirectory"];
	    }
	}

}

export namespace launch {
	
	export class GameLaunchSnapshot {
	    Revision: number;
	    Phase: number;
	    Title: string;
	    Message: string;
	    VersionId: string;
	    AccountName: string;
	    ProcessId: number;
	
	    static createFrom(source: any = {}) {
	        return new GameLaunchSnapshot(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Revision = source["Revision"];
	        this.Phase = source["Phase"];
	        this.Title = source["Title"];
	        this.Message = source["Message"];
	        this.VersionId = source["VersionId"];
	        this.AccountName = source["AccountName"];
	        this.ProcessId = source["ProcessId"];
	    }
	}
	export class GameMemoryDecision {
	    IsAutomatic: boolean;
	    MaximumMemoryMb: number;
	    TotalMemoryMb: number;
	    AvailableMemoryMb: number;
	    ReservedMemoryMb: number;
	    IsMemoryTight: boolean;
	
	    static createFrom(source: any = {}) {
	        return new GameMemoryDecision(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.IsAutomatic = source["IsAutomatic"];
	        this.MaximumMemoryMb = source["MaximumMemoryMb"];
	        this.TotalMemoryMb = source["TotalMemoryMb"];
	        this.AvailableMemoryMb = source["AvailableMemoryMb"];
	        this.ReservedMemoryMb = source["ReservedMemoryMb"];
	        this.IsMemoryTight = source["IsMemoryTight"];
	    }
	}
	export class LaunchResult {
	    Success: boolean;
	    Message: string;
	
	    static createFrom(source: any = {}) {
	        return new LaunchResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Success = source["Success"];
	        this.Message = source["Message"];
	    }
	}
	export class SystemMemorySnapshot {
	    TotalMemoryMb: number;
	    AvailableMemoryMb: number;
	
	    static createFrom(source: any = {}) {
	        return new SystemMemorySnapshot(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.TotalMemoryMb = source["TotalMemoryMb"];
	        this.AvailableMemoryMb = source["AvailableMemoryMb"];
	    }
	}

}

export namespace models {
	
	export class MinecraftVersion {
	    id: string;
	    type: string;
	    url: string;
	    // Go type: time
	    time: any;
	    // Go type: time
	    releaseTime: any;
	
	    static createFrom(source: any = {}) {
	        return new MinecraftVersion(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.type = source["type"];
	        this.url = source["url"];
	        this.time = this.convertValues(source["time"], null);
	        this.releaseTime = this.convertValues(source["releaseTime"], null);
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}

}

export namespace modpack {
	
	export class ModpackContentItem {
	    Category: string;
	    RelativePath: string;
	    Name: string;
	    SizeBytes: number;
	    IsDirectory: boolean;
	
	    static createFrom(source: any = {}) {
	        return new ModpackContentItem(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Category = source["Category"];
	        this.RelativePath = source["RelativePath"];
	        this.Name = source["Name"];
	        this.SizeBytes = source["SizeBytes"];
	        this.IsDirectory = source["IsDirectory"];
	    }
	}
	export class ModpackExportOptions {
	    Format: number;
	    PackName: string;
	    PackVersion: string;
	    Author: string;
	    UpdateLink: string;
	    Description: string;
	    IconPngPath: string;
	    MinecraftVersion: string;
	    LoaderName: string;
	    LoaderVersion: string;
	    IncludedPaths: string[];
	    ResolveModrinthLinks: boolean;
	
	    static createFrom(source: any = {}) {
	        return new ModpackExportOptions(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Format = source["Format"];
	        this.PackName = source["PackName"];
	        this.PackVersion = source["PackVersion"];
	        this.Author = source["Author"];
	        this.UpdateLink = source["UpdateLink"];
	        this.Description = source["Description"];
	        this.IconPngPath = source["IconPngPath"];
	        this.MinecraftVersion = source["MinecraftVersion"];
	        this.LoaderName = source["LoaderName"];
	        this.LoaderVersion = source["LoaderVersion"];
	        this.IncludedPaths = source["IncludedPaths"];
	        this.ResolveModrinthLinks = source["ResolveModrinthLinks"];
	    }
	}
	export class ModpackExportProfile {
	    packName?: string;
	    packVersion?: string;
	    author?: string;
	    description?: string;
	    updateLink?: string;
	    format?: number;
	    resolveModrinthLinks?: boolean;
	    excludedPaths?: string[];
	
	    static createFrom(source: any = {}) {
	        return new ModpackExportProfile(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.packName = source["packName"];
	        this.packVersion = source["packVersion"];
	        this.author = source["author"];
	        this.description = source["description"];
	        this.updateLink = source["updateLink"];
	        this.format = source["format"];
	        this.resolveModrinthLinks = source["resolveModrinthLinks"];
	        this.excludedPaths = source["excludedPaths"];
	    }
	}
	export class ModpackExportResult {
	    OutputPath: string;
	    DeclaredFiles: number;
	    OverrideFiles: number;
	    Warnings: string[];
	
	    static createFrom(source: any = {}) {
	        return new ModpackExportResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.OutputPath = source["OutputPath"];
	        this.DeclaredFiles = source["DeclaredFiles"];
	        this.OverrideFiles = source["OverrideFiles"];
	        this.Warnings = source["Warnings"];
	    }
	}

}

export namespace monitoring {
	
	export class MemorySnapshot {
	    LauncherMemoryMb: number;
	    JvmMemoryMb: number;
	    JavaProcessCount: number;
	
	    static createFrom(source: any = {}) {
	        return new MemorySnapshot(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.LauncherMemoryMb = source["LauncherMemoryMb"];
	        this.JvmMemoryMb = source["JvmMemoryMb"];
	        this.JavaProcessCount = source["JavaProcessCount"];
	    }
	}

}

export namespace music {
	
	export class MusicTrack {
	    FilePath: string;
	    FileSize: number;
	    // Go type: time
	    LastModified: any;
	
	    static createFrom(source: any = {}) {
	        return new MusicTrack(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.FilePath = source["FilePath"];
	        this.FileSize = source["FileSize"];
	        this.LastModified = this.convertValues(source["LastModified"], null);
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}

}

export namespace network {
	
	export class MinecraftServerStatus {
	    Motd: string;
	    VersionName: string;
	    ProtocolVersion: number;
	    OnlinePlayers: number;
	    MaxPlayers: number;
	    IconPath: string;
	
	    static createFrom(source: any = {}) {
	        return new MinecraftServerStatus(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Motd = source["Motd"];
	        this.VersionName = source["VersionName"];
	        this.ProtocolVersion = source["ProtocolVersion"];
	        this.OnlinePlayers = source["OnlinePlayers"];
	        this.MaxPlayers = source["MaxPlayers"];
	        this.IconPath = source["IconPath"];
	    }
	}

}

export namespace world {
	
	export class WorldInfo {
	    Name: string;
	    DirectoryPath: string;
	    OwnerVersionId: string;
	    // Go type: time
	    LastPlayed: any;
	    IconPath: string;
	
	    static createFrom(source: any = {}) {
	        return new WorldInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.Name = source["Name"];
	        this.DirectoryPath = source["DirectoryPath"];
	        this.OwnerVersionId = source["OwnerVersionId"];
	        this.LastPlayed = this.convertValues(source["LastPlayed"], null);
	        this.IconPath = source["IconPath"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}

}

