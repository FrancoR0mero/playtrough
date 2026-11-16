namespace Playtrough.Api.Models;

// Friends is retained only to read existing databases; the API accepts Public or Private.
public enum Visibility { Public, Friends, Private }
public enum PrivacyScope { Profile, History }
