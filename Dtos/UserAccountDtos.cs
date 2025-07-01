// src/AutomotiveServices.Api/Dtos/UserAccountDtos.cs
using System.ComponentModel.DataAnnotations;

namespace AutomotiveServices.Api.Dtos;

public class MergeAnonymousDataRequestDto
{
    // Client sends the *raw anonymous session token* it was using.
    [Required(AllowEmptyStrings = false, ErrorMessage = "AnonymousSessionToken is required to merge data.")]
    public string AnonymousSessionToken { get; set; } = string.Empty; 
}

// MergeAnonymousDataResponseDto can stay in AnonymousCartDtos.cs or move here too.
// For this example, assuming it's already correctly defined in AnonymousCartDtos.cs
// If not, its definition would be:
/*
public class MergeAnonymousDataResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public MergeDetails? Details { get; set; }

    public class MergeDetails
    {
        public int CartItemsTransferred { get; set; }
        public int DuplicatesHandled { get; set; }
        public bool PreferencesTransferred { get; set; }
    }
}
*/