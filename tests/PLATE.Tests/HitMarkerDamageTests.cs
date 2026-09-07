using PLATE.Client.Overlay;
using Xunit;

namespace PLATE.Tests
{
    public class HitMarkerDamageTests
    {
        [Theory]
        [InlineData(0, false, true, 80, 20, 3, "F:0 B:0 A:3")]
        [InlineData(50, false, true, 80, 20, 3, "F:40 B:10 A:3")]
        [InlineData(100, false, true, 40, 10, 3, "F:80 B:20 A:3")]
        [InlineData(2, true, false, 0, 0, 0, "F:0 B:2 A:0")]
        [InlineData(25, false, false, 0, 0, 4, "F:25 B:0 A:4")]
        public void Label_uses_actual_health_result_and_separate_durability(float applied,
            bool blocked, bool breakdown, float wound, float blunt, float armor, string expected)
        {
            Assert.Equal(expected, HitMarkerDamage.Label(applied, blocked, breakdown,
                wound, blunt, armor));
        }

        [Fact]
        public void Armor_stack_is_summed_once_and_nested_hits_do_not_mix()
        {
            var victim = new object();
            var collider = new object();
            var parent = HitMarkerDamage.Begin(victim, collider);
            try
            {
                HitMarkerDamage.RecordArmor(collider, 40, 38);
                HitMarkerDamage.RecordArmor(collider, 15, 12);
                HitMarkerDamage.RecordArmor(new object(), 100, 0);
                var child = HitMarkerDamage.Begin(victim, collider);
                try
                {
                    HitMarkerDamage.RecordArmor(collider, 10, 9);
                    Assert.Equal(1, HitMarkerDamage.ConsumeArmor(victim, collider));
                }
                finally { child.End(); }
                Assert.Equal(0, HitMarkerDamage.ConsumeArmor(new object(), collider));
                Assert.Equal(5, HitMarkerDamage.ConsumeArmor(victim, collider));
                Assert.Equal(0, HitMarkerDamage.ConsumeArmor(victim, collider));
            }
            finally { parent.End(); }
            Assert.Equal(0, HitMarkerDamage.ConsumeArmor(victim, collider));
        }
    }
}
