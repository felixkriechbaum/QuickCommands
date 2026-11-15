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

  depends_on macos: ">= :sonoma"

  app "QuickCommands.app"

  # Removes the QuickCommands line from .zshrc/.bashrc/PowerShell profile. Your commands are kept.
  uninstall_preflight do
    system_command "#{appdir}/QuickCommands.app/Contents/MacOS/QuickCommands",
                   args:         ["--remove-integration"],
                   must_succeed: false
  end

  zap trash: "~/.config/QuickCommands"

  caveats <<~EOS
    QuickCommands is not notarized by Apple, so macOS blocks the first launch.
    Open System Settings → Privacy & Security and click "Open Anyway" once.

    Your commands are stored in ~/.quickcommands and are kept on uninstall.
  EOS
end
