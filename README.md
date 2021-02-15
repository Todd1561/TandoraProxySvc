# Tandora Proxy
* This program is used to interface between the pianobar-windows open source application and any program that can send TCP commands.  
* This would generally be useful in a headless home automation A/V setup to bring Pandora to your sound system.  
* This application works by leveraging the Telnet server that can be enabled in Windows and remotely starting/sending commands to Pianobar. 
* I'll outline the basic steps below to get everything installed.

1. Enable the Windows Telnet service (3rd party telnet servers should work, too)
1. Set the Telnet service to start automatically in services.msc
1. Disable the idle timeout setting by entering `tlntadmn.exe config timeoutactive=no` in an elevated command prompt
1. Download the latest pianobar-windows build from [pianobar-windows](https://github.com/thedmd/pianobar-windows/releases) and save it to a folder along with `TandoraProxy.exe`
1. Install the TandoraProxy service by running `C:\Windows\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe TandoraProxySvc.exe`
1. Modify `tandoraproxy.cfg` to suit your needs.
1. Remember to open the TCP port you decide to use for TandoraProxy (1561 by default) on any relevant firewalls
1. Once the program is running you can send the below commands to control Pianobar.  These are just sent as raw ASCII via TCP.  You'll get a response back with the current status of Pianobar/TandoraProxy.

From here you can use whatever language you want that can work with TCP sockets to interace with TandoraProxy/Pianobar.
	
### Commands:	
* "update": have TandoraProxy query pianobar-windows for the current song, station, play time and whether or not playback is active and a list of your Pandora stations.
* "playpause": toggle playing and pausing music playback.
* "next": play next song.
* "thumbsup": like current song.
* "thumbsdown": dislike current song.
* "change station *__station name__*": change the current station to station *???*.

## Known Issues:
Doesn't seem to work with the latest version (2020.04.20) of Pianobar-Windows, stick with version 2019.05.03.  For some reason the key commands are erradically accepted by pianobar and the responses are erratic as well.  I suspect something has changed with pianobar that makes it incompatible with the telnet library I'm Currently using.  Need to investigate more.
	
## Author
Todd Nelson  
todd@toddnelson.net  
https://toddnelson.net