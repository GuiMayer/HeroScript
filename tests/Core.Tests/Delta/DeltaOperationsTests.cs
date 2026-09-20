using System;
using System.Collections.Generic;
using System.Text.Json;
using Core.Config.Delta;
using Xunit;

namespace Core.Tests.Delta
{
    /// <summary>
    /// Tests for Delta operations (REPLACE, MERGE_DEEP, FIELD_DELETE, etc.)
    /// </summary>
    public class DeltaOperationsTests
    {
        // ========================================
        // REPLACE OPERATION TESTS
        // ========================================

        [Fact]
        public void DeltaApply_Replace_ReplacesEntireResource()
        {
            var baseResource = JsonDocument.Parse(@"{
                ""name"": ""BaseItem"",
                ""value"": 100,
                ""nested"": { ""key"": ""old"" }
            }").RootElement;

            var delta = new DeltaDefinition
            {
                Operation = DeltaOperationType.REPLACE,
                Data = new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonDocument.Parse("\"NewItem\"").RootElement,
                    ["value"] = JsonDocument.Parse("200").RootElement
                }
            };

            var result = DeltaMerger.ApplyDelta(baseResource, delta, "TEST_RESOURCE", strictMode: true);

            Assert.True(result.HasValue);
            Assert.Equal("NewItem", result.Value.GetProperty("name").GetString());
            Assert.Equal(200, result.Value.GetProperty("value").GetInt32());
            Assert.False(result.Value.TryGetProperty("nested", out _));
        }

        // ========================================
        // MERGE_DEEP OPERATION TESTS
        // ========================================

        [Fact]
        public void DeltaApply_MergeDeep_MergesNestedObjects()
        {
            var baseResource = JsonDocument.Parse(@"{
                ""stats"": {
                    ""health"": 100,
                    ""mana"": 50
                },
                ""name"": ""Hero""
            }").RootElement;

            var delta = new DeltaDefinition
            {
                Operation = DeltaOperationType.MERGE_DEEP,
                Data = new Dictionary<string, JsonElement>
                {
                    ["stats"] = JsonDocument.Parse(@"{
                        ""mana"": 75,
                        ""stamina"": 60
                    }").RootElement
                }
            };

            var result = DeltaMerger.ApplyDelta(baseResource, delta, "TEST_RESOURCE", strictMode: true);

            Assert.True(result.HasValue);
            var stats = result.Value.GetProperty("stats");
            Assert.Equal(100, stats.GetProperty("health").GetInt32());
            Assert.Equal(75, stats.GetProperty("mana").GetInt32());
            Assert.Equal(60, stats.GetProperty("stamina").GetInt32());
            Assert.Equal("Hero", result.Value.GetProperty("name").GetString());
        }

        [Fact]
        public void DeltaApply_MergeDeep_PreservesUnmodifiedFields()
        {
            var baseResource = JsonDocument.Parse(@"{
                ""id"": 1,
                ""name"": ""Original"",
                ""config"": {
                    ""enabled"": true,
                    ""timeout"": 30
                }
            }").RootElement;

            var delta = new DeltaDefinition
            {
                Operation = DeltaOperationType.MERGE_DEEP,
                Data = new Dictionary<string, JsonElement>
                {
                    ["config"] = JsonDocument.Parse(@"{
                        ""timeout"": 60
                    }").RootElement
                }
            };

            var result = DeltaMerger.ApplyDelta(baseResource, delta, "TEST_RESOURCE", strictMode: true);

            Assert.True(result.HasValue);
            Assert.Equal(1, result.Value.GetProperty("id").GetInt32());
            Assert.Equal("Original", result.Value.GetProperty("name").GetString());
            
            var config = result.Value.GetProperty("config");
            Assert.True(config.GetProperty("enabled").GetBoolean());
            Assert.Equal(60, config.GetProperty("timeout").GetInt32());
        }

        // ========================================
        // FIELD_DELETE OPERATION TESTS
        // ========================================

        [Fact]
        public void DeltaApply_FieldDelete_RemovesSpecifiedField()
        {
            var baseResource = JsonDocument.Parse(@"{
                ""id"": 1,
                ""name"": ""Item"",
                ""deprecated"": true
            }").RootElement;

            var delta = new DeltaDefinition
            {
                Operation = DeltaOperationType.FIELD_DELETE,
                TargetPath = "deprecated"
            };

            var result = DeltaMerger.ApplyDelta(baseResource, delta, "TEST_RESOURCE", strictMode: true);

            Assert.True(result.HasValue);
            Assert.Equal(1, result.Value.GetProperty("id").GetInt32());
            Assert.Equal("Item", result.Value.GetProperty("name").GetString());
            Assert.False(result.Value.TryGetProperty("deprecated", out _));
        }

        // ========================================
        // VALIDATION TESTS
        // ========================================

        [Fact]
        public void DeltaValidator_Replace_RequiresData()
        {
            var delta = new DeltaDefinition
            {
                Operation = DeltaOperationType.REPLACE
            };

            var result = DeltaValidator.Validate("TEST", delta, strictMode: true);

            Assert.False(result.IsValid);
            Assert.True(result.Errors.Count > 0);
            Assert.Contains("data", result.Errors[0].ToLower());
        }

        [Fact]
        public void DeltaValidator_FieldDelete_RequiresTargetPath()
        {
            var delta = new DeltaDefinition
            {
                Operation = DeltaOperationType.FIELD_DELETE
            };

            var result = DeltaValidator.Validate("TEST", delta, strictMode: true);

            Assert.False(result.IsValid);
            Assert.True(result.Errors.Count > 0);
        }

        [Theory]
        [InlineData("effects[-1]")]
        [InlineData("effects[abc]")]
        [InlineData("effects[0")]
        [InlineData("params..value")]
        [InlineData("params.")]
        [InlineData("[0]")]
        [InlineData("$params.value")]
        public void DeltaValidator_FieldDelete_RejectsInvalidTargetPath(string targetPath)
        {
            var delta = new DeltaDefinition
            {
                Operation = DeltaOperationType.FIELD_DELETE,
                TargetPath = targetPath
            };

            var result = DeltaValidator.Validate("TEST", delta, strictMode: true);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("Target path", StringComparison.Ordinal));
        }

        [Fact]
        public void DeltaValidator_MergeDeep_RequiresData()
        {
            var delta = new DeltaDefinition
            {
                Operation = DeltaOperationType.MERGE_DEEP
            };

            var result = DeltaValidator.Validate("TEST", delta, strictMode: true);

            Assert.False(result.IsValid);
            Assert.True(result.Errors.Count > 0);
        }

        // ========================================
        // INHERITANCE CHAIN TESTS
        // ========================================

        [Fact]
        public void DeltaInheritance_MultipleConfigs_AppliesInOrder()
        {
            // Base config
            var baseResource = JsonDocument.Parse(@"{
                ""damage"": 10,
                ""speed"": 1.0
            }").RootElement;

            // First mod: increase damage
            var delta1 = new DeltaDefinition
            {
                Operation = DeltaOperationType.MERGE_DEEP,
                Data = new Dictionary<string, JsonElement>
                {
                    ["damage"] = JsonDocument.Parse("15").RootElement
                }
            };

            var result1 = DeltaMerger.ApplyDelta(baseResource, delta1, "MOD1", strictMode: true);
            Assert.True(result1.HasValue);

            // Second mod: add speed boost
            var delta2 = new DeltaDefinition
            {
                Operation = DeltaOperationType.MERGE_DEEP,
                Data = new Dictionary<string, JsonElement>
                {
                    ["speed"] = JsonDocument.Parse("1.5").RootElement,
                    ["bonus"] = JsonDocument.Parse("true").RootElement
                }
            };

            var result2 = DeltaMerger.ApplyDelta(result1.Value, delta2, "MOD2", strictMode: true);
            Assert.True(result2.HasValue);

            Assert.Equal(15, result2.Value.GetProperty("damage").GetInt32());
            Assert.Equal(1.5, result2.Value.GetProperty("speed").GetDouble());
            Assert.True(result2.Value.GetProperty("bonus").GetBoolean());
        }

        // ========================================
        // DELETE OPERATION TESTS
        // ========================================

        [Fact]
        public void DeltaApply_Delete_ReturnsNull()
        {
            var baseResource = JsonDocument.Parse(@"{
                ""id"": 1,
                ""name"": ""Item""
            }").RootElement;

            var delta = new DeltaDefinition
            {
                Operation = DeltaOperationType.DELETE
            };

            var result = DeltaMerger.ApplyDelta(baseResource, delta, "TEST_RESOURCE", strictMode: true);

            Assert.False(result.HasValue);
        }
    }
}
