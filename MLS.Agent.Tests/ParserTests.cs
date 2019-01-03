using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace MLS.Agent.Tests
{
    public class CommandLineParserTests
    {
        private readonly ITestOutputHelper _output;
        private readonly TestConsole _console = new TestConsole();
        private StartupOptions _options;
        private readonly Parser _parser;
        private string _repo;

        public CommandLineParserTests(ITestOutputHelper output)
        {
            _output = output;
            _parser = Program.CreateParser((options, invocationContext) =>
            {
                _options = options;
            },
            (repo, c) =>
            {
                _repo = repo;
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task Parse_empty_command_line_has_sane_defaults()
        {
            await _parser.InvokeAsync("", _console);

            _options.Production.Should().BeFalse();
        }

        [Fact]
        public async Task Parse_production_mode_flag_switches_option_to_production()
        {
            await _parser.InvokeAsync("--production", _console);

            _options.Production.Should().BeTrue();
        }

        [Fact]
        public async Task Parse_root_directory_with_a_valid_path_succeeds()
        {
            var path = TestAssets.BasicConsole.FullName;
            await _parser.InvokeAsync(new[] { "--root-directory", path }, _console);
            _options.RootDirectory.FullName.Should().Be(path);
        }

        [Fact]
        public async Task Parse_empty_command_line_has_current_directory_as_root_directory()
        {
            await _parser.InvokeAsync("", _console);
            _options.RootDirectory.FullName.Should().Be(Directory.GetCurrentDirectory());
        }

        [Fact]
        public async Task Parse_root_directory_with_a_non_existing_path_fails()
        {
            await _parser.InvokeAsync(new[] { "--root-directory", "INVALIDPATH" }, _console);
            _options.Should().BeNull();
        }

        [Fact]
        public async Task Parse_language_service_mode_flag_switches_option_to_language_service()
        {
            await _parser.InvokeAsync("--language-service", _console);
            _options.IsLanguageService.Should().BeTrue();
        }

        [Fact]
        public async Task Parse_key_without_parameter_fails_the_parse()
        {
            _parser.Parse("-k")
                   .Errors
                   .Should()
                   .Contain(e => e.Message == "Required argument missing for option: -k");

            _parser.Parse("--key")
                   .Errors
                   .Should()
                   .Contain(e => e.Message == "Required argument missing for option: --key");
        }

        [Fact]
        public async Task Parse_key_with_parameter_succeeds()
        {
            await _parser.InvokeAsync(new[] { "-k", "abc123" }, _console);
            _options.Key.Should().Be("abc123");

            await _parser.InvokeAsync(new[] { "--key", "abc123" }, _console);
            _options.Key.Should().Be("abc123");
        }

        [Fact]
        public async Task AiKey_defaults_to_null()
        {
            await _parser.InvokeAsync("", _console);
            _options.ApplicationInsightsKey.Should().BeNull();
        }

        [Fact]
        public async Task Parse_application_insights_key_without_parameter_fails_the_parse()
        {
            var result = _parser.Parse("--ai-key");

            _output.WriteLine(_console.Out.ToString());

            result.Errors.Should().Contain(e => e.Message == "Required argument missing for option: --ai-key");
        }

        [Fact]
        public async Task Parse_aiKey_with_parameter_succeeds()
        {
            await _parser.InvokeAsync(new[] { "--ai-key", "abc123" }, _console);
            _options.ApplicationInsightsKey.Should().Be("abc123");
        }

        [Fact]
        public async Task GitHub_handler_not_run_if_argument_is_missing()
        {
            _repo = "value";
            await _parser.InvokeAsync("github");
            _repo.Should().Be("value");
        }

        [Fact]
        public async Task GitHub_handler_run_if_argument_is_present()
        {
            _repo = "value";
            await _parser.InvokeAsync("github roslyn");
            _repo.Should().Be("roslyn");
        }
    }
}
