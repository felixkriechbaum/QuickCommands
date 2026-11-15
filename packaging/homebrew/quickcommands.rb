cask "quickcommands" do
  arch arm: "arm64", intel: "x64"

  version "__VERSION__"
  sha256 arm:   "__SHA256_ARM64__",
         intel: "__SHA256_X64__"

  url "https://github.com/felixkriechbaum/QuickCommands/releases/download/v#{version}/QuickCommands-#{version}-macos-#{arch}.zip"
  name "QuickCommands"
  desc "Define your own terminal commands for zsh, bash and PowerShell"
  homepage "https://github.com/felixkriechbaum/QuickCommands"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on macos: :sonoma

  app "QuickCommands.app"

  zap trash: "~/.config/QuickCommands"

  # Removing the profile line is not done in an uninstall stanza on purpose: those also run on
  # `brew upgrade`, which would switch the commands off after every update.
  caveats <<~EOS
    QuickCommands is not notarized by Apple, so macOS blocks the first launch.
    Open System Settings → Privacy & Security and click "Open Anyway" once.

    Before uninstalling, remove the shell integration in QuickCommands
    (Settings → Remove integration completely). Your commands in ~/.quickcommands are kept.
  EOS
end
