namespace ConquerMono.Conquer.Data.Models;

/// <summary>
/// Defines the visual equipment state of a character.
/// </summary>
public interface IRoleAppearance
{
    uint Look { get; }      // Base body model (e.g., 1003, 1004 for male/female)
    uint ArmorId { get; }   // Body armor ID
    uint ArmetId { get; }   // Headgear ID
    uint RWeaponId { get; } // Right-hand weapon ID
    uint LWeaponId { get; } // Left-hand weapon ID (or shield)
    uint MountId { get; }   // Steed ID
}