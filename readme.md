# Introduction

I've created The Familiar because I was tired of remembering complex commands written to `cmd` or `powershell`. The Familiar remembers all commands you type, forever*. The history survives reboots and can be shared to multiple machines, which is convenient if you use VMs or need to reset your system often. **Warning**: The Familiar is in alpha mode. It works - I use it all the time - but it's not polished.

\* *Technically, it remembers while your files are good.*

# Installation

## 1. Install Scoop from https://scoop.sh/

Scoop is a bunch of non-invasive Powershell scripts that helps with installation of command line tools. As of this writing, Scoop is less invasive than Chocolatey and `dotnet tool`, which explains why I choose that over the others.

After you installed, make sure `scoop` is in your path.

## 2. Install The Familiar

Run the following from `cmd` or `powershell`:

    scoop install https://raw.githubusercontent.com/fernacolo/familiar/master/scoop/fam.json

Then run `fam -h` to check if the familiar is in the path. If not, close and open the prompt and try again. There should be no need to reboot or change your path.

## 3. [optional] Connect to a Shared Directory

Let's say you use **OneDrive** or **DropBox**. In order to automatically store all your commands there, do this:

    md "%OneDrive%\familiar"
    fam --connect "%OneDrive%"

Now all commands you write will be stored there. Do this for other machines, and they will all see the same history. You can choose any directory, even a shared folder such as `\\myserver\shared\familiar`.

## 4. Use The Familiar

Just run `fam` and you will see a small window attached to your prompt. Everything you type there will be recorded and executed in the prompt. If you need to type a password or interact with the prompt window directly, just use the mouse to click on the prompt window. Everything you type in the prompt window directly will **NOT** be recorded by the Familiar.

The Familiar embraces the non-invasion principle. The tool does not change the registry and does not intercept anything in the prompt process. Instead, it attaches to the window and uses **Win32 APIs** to simulate keystrokes.
