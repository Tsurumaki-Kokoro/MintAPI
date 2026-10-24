using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MintAPI.Models.Entities;

[Table("user_osu_info_history")]
public class UserOsuInfoHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("osu_uid")]
    public int OsuUserId { get; set; }

    [Column("game_mode")]
    public int GameMode { get; set; }

    [Column("country_rank")]
    public int? CountryRank { get; set; }

    [Column("global_rank")]
    public int? GlobalRank { get; set; }

    [Column("pp")]
    public double? Pp { get; set; }

    [Column("accuracy")]
    public double? Accuracy { get; set; }

    [Column("play_count")]
    public int? PlayCount { get; set; }

    [Column("play_time")]
    public int? PlayTime { get; set; }

    [Column("total_hits")]
    public long? TotalHits { get; set; }

    [Column("ranked_score")]
    public long? RankedScore { get; set; }

    [Column("total_score")]
    public long? TotalScore { get; set; }

    [Column("observed_at")]
    public DateTimeOffset? ObservedAt { get; set; }

    [Column("date")]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
}
