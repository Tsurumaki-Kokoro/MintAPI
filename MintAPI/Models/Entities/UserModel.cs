using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MintAPI.Models.Entities;

[Table("user")]
public class UserModel
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("osu_uid")]
    public int OsuUserId { get; set; }

    [Column("game_mode")]
    public int GameMode { get; set; } = 0;

    [Required]
    [MaxLength(255)]
    [Column("platform")]
    public string Platform { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    [Column("platform_uid")]
    public string PlatformUid { get; set; } = string.Empty;
}
