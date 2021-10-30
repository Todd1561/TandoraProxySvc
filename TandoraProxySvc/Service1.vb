Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Public Class Service1

    Dim pianobarStations As New ArrayList, pSong As String = "", pAlbum As String = "", pArtist As String = "", pStation As String = "", pErrorTxt As String = ""
    Dim cmd As String = "", pCurTime As String = "/", pIsPlaying As Boolean = False, pianobarLast As String = ""
    Dim pianoPath As String = AppDomain.CurrentDomain.BaseDirectory & "Pianobar.exe", pianoServ As String = "localhost", pianoPort As String = "23", logFile = ""
    Dim username As String = "", password As String = "", tandoraPort As String = "1561", isTandoraActive As Boolean = False
    Dim tcpListener

    Protected Overrides Sub OnStart(ByVal args() As String)

        Dim help As String = "TandoraProxy, Ver. 2.7 (10/26/2021), toddnelson.net.  https://toddnelson.net" & vbCrLf & vbCrLf &
            "Place a tandoraproxy.cfg file in the same directory as TandoraProxySvc.exe with the following settings: " & vbCrLf & vbCrLf &
            "logfile=<absolute path to file to log all service activity>" & vbCrLf &
            "pianopath=<absolute path to Pianobar exe> (default: current directory)" & vbCrLf &
            "pianoserv=<address of Pianobar Telnet server> (default: localhost)" & vbCrLf &
            "pianoport=<TCP port of Pianobar Telnet server> (default: 23)" & vbCrLf &
            "tandoraport=<TCP port for TandoraProxy> (default: 1561)" & vbCrLf &
            "username=<Pianobar Telnet server username> (Required)" & vbCrLf &
            "password=<Pianobar Telnet server password> (Required)"

        If Not EventLog.SourceExists("TandoraProxy") Then EventLog.CreateEventSource("TandoraProxy", "Application")

        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory)

        If Not File.Exists("tandoraproxy.cfg") Then
            EventLog.WriteEntry("TandoraProxy", help, EventLogEntryType.Error, 1, 1)
            [Stop]()
            Exit Sub
        End If

        For Each line As String In File.ReadLines("tandoraproxy.cfg")
            If line.Substring(0, line.IndexOf("=") + 1) = "pianopath=" And line.Length > 10 Then pianoPath = line.Substring(line.IndexOf("=") + 1)
            If line.Substring(0, line.IndexOf("=") + 1) = "pianoserv=" And line.Length > 10 Then pianoServ = line.Substring(line.IndexOf("=") + 1)
            If line.Substring(0, line.IndexOf("=") + 1) = "pianoport=" And line.Length > 10 Then pianoPort = line.Substring(line.IndexOf("=") + 1)
            If line.Substring(0, line.IndexOf("=") + 1) = "tandoraport=" And line.Length > 12 Then tandoraPort = line.Substring(line.IndexOf("=") + 1)
            If line.Substring(0, line.IndexOf("=") + 1) = "username=" And line.Length > 9 Then username = line.Substring(line.IndexOf("=") + 1)
            If line.Substring(0, line.IndexOf("=") + 1) = "password=" And line.Length > 9 Then password = line.Substring(line.IndexOf("=") + 1)
            If line.Substring(0, line.IndexOf("=") + 1) = "logfile=" And line.Length > 8 Then logFile = line.Substring(line.IndexOf("=") + 1)
        Next line

        If username = "" Or password = "" Then
            EventLog.WriteEntry("TandoraProxy", "Username and password required." & vbCrLf & vbCrLf & help, EventLogEntryType.Error, 1, 1)
            [Stop]()
            Exit Sub
        End If

        If Not Regex.IsMatch(pianoPath.ToLower, "[a-z]:.*") Then
            EventLog.WriteEntry("TandoraProxy", "Path to Pianobar.exe needs to be a local drive letter.", EventLogEntryType.Error, 1, 1)
            [Stop]()
            Exit Sub
        End If

        'Create new thread to listen for TandoraProxy clients 
        'run in the background so they auto-terminate when the service stops
        Dim thdControl As Thread = New Thread(AddressOf ListenForClients) With {.IsBackground = True}
        thdControl.Start()

        'create new thread to send commands to pianobar and monitor responses
        Dim thdMonitorPianobar As Thread = New Thread(AddressOf MonitorPianobar) With {.IsBackground = True}
        thdMonitorPianobar.Start()

    End Sub

    Sub MonitorPianobar()

        Dim pingPandora As New NetworkInformation.Ping
        Dim pingReply As NetworkInformation.PingReply
10:

        Try

            pingReply = pingPandora.Send("tuner.pandora.com")

            If pingReply.Status <> NetworkInformation.IPStatus.Success Then
                EventLog.WriteEntry("TandoraProxy", "tuner.pandora.com could not be reached, waiting 3 minutes and trying again." & vbCrLf & vbCrLf & "Response: " & pingReply.Status.ToString, EventLogEntryType.Error, 1, 1)
                Thread.Sleep(180000)
                GoTo 10
            End If

        Catch ex As Exception
            EventLog.WriteEntry("TandoraProxy", "tuner.pandora.com could not be reached, waiting 3 minutes and trying again." & vbCrLf & vbCrLf & "Response: " & ex.Message, EventLogEntryType.Error, 1, 1)
            Thread.Sleep(180000)

            GoTo 10
        End Try

        isTandoraActive = True

        Dim tc As MinimalisticTelnet.TelnetConnection = New MinimalisticTelnet.TelnetConnection(pianoServ, pianoPort)
        Dim s As String = tc.Login(username, password, 600)
        Console.Write(s)
        Dim prompt As String = s.TrimEnd()
        prompt = s.Substring(prompt.Length - 1, 1)
        If prompt <> "$" AndAlso prompt <> ">" Then Throw New Exception("Connection failed")
        prompt = ""

        If tc.IsConnected Then
            tc.WriteLine(pianoPath.Substring(0, 2))
            tc.WriteLine("""" & pianoPath & """") 'start pianobar.exe
            Threading.Thread.Sleep(3000)

            'set pianobar to "high" priority to avoid stuttering audio
            Process.Start("wmic", "process where name=""pianobar.exe"" CALL setpriority 128")

            Dim resp As String = ""

            'keep looping, taking response from pandora and sending commands as I get them from the API
            'commands can be broken up with pipes and comas injected to create delays. 
            '(useful for changing stations And needing to wait a split second for list to build in pianobar UI)
            While True

                If cmd <> "" Then
                    For Each c As String In cmd.Split("|")
                        If c = "," Then Thread.Sleep(50) Else tc.Write(c)
                    Next

                    cmd = ""
                End If

                resp = tc.Read

                If resp <> "" Then

                    pErrorTxt = ""

                    If resp.Contains("Error: Access denied. Try again later.") Then
                        'handle odd issue where pandora won't play a station and reload the station list
                        cmd = "s"
                        pErrorTxt = "This station is currently unavailable, try again later."
                        pSong = ""
                        pArtist = ""
                        pAlbum = ""
                        pStation = ""
                        pCurTime = "/"
                        pIsPlaying = False
                    End If

                    'parse out station list
                    If Regex.IsMatch(resp, "\s(\d+)\).{5}(.*?)\n") Then
                            For Each station As Match In Regex.Matches(resp, "\s(\d+)\).{5}(.*?)\n")

                                'Detected the first station, clear the array
                                If station.Groups(1).Value.Trim = "0" Then pianobarStations.Clear()
                                pianobarStations.Add(station.Groups(2).Value.Trim)

                            Next
                        End If

                        'parse out current song info
                        If Regex.IsMatch(resp, "\|\>  ""(.*?)"" by ""(.*?)"" on ""(.*?)""") Then
                            Dim m As Match = Regex.Match(resp, "\|\>  ""(.*?)"" by ""(.*?)"" on ""(.*?)""")
                            pSong = m.Groups(1).Value
                            pArtist = m.Groups(2).Value
                            pAlbum = m.Groups(3).Value
                        End If

                        'parse out current station
                        If Regex.IsMatch(resp, "\|\>  Station ""(.*?)""") Then
                            Dim m As Match = Regex.Match(resp, "\|\>  Station ""(.*?)""")
                            pStation = m.Groups(1).Value
                        End If

                        'parse out current play time
                        If Regex.IsMatch(resp, "#\s+([-\d:]+)/([-\d:]+)") Then
                            Dim lastPCurTime As String = pCurTime
                            Dim m As Match = Regex.Match(resp, "#\s+([-\d:]+)/([-\d:]+)")
                            pCurTime = m.Groups(1).Value & "/" & m.Groups(2).Value
                            If lastPCurTime <> pCurTime Then pIsPlaying = True Else pIsPlaying = False
                        Else
                            If logFile <> "" Then File.AppendAllText(logFile, vbCrLf & vbCrLf & "*** Start Update From Pianobar On " & Date.Now.ToString() & " ***" & vbCrLf & resp.Trim & vbCrLf & "*** End Update From Pianobar ***")
                        End If

                        Console.Write(resp)

                        pianobarLast = resp
                    End If

            End While

        End If

        While tc.IsConnected AndAlso prompt.Trim() <> "exit"
            Console.Write(tc.Read())
            prompt = Console.ReadLine()
            tc.WriteLine(prompt)
            Console.Write(tc.Read())
        End While

        Console.WriteLine("***DISCONNECTED")
        Console.ReadLine()
    End Sub

    Sub ListenForClients()
        tcpListener = New TcpListener(IPAddress.Any, tandoraPort)

        tcpListener.Start()

        While True
            'blocks until a client has connected to the server
            Dim client As TcpClient = tcpListener.AcceptTcpClient
            'create a thread to handle communication with connected client
            Dim clientThread As Thread = New Thread(New ParameterizedThreadStart(AddressOf HandleClientComm))
            clientThread.Start(client)
        End While

    End Sub

    Private Sub HandleClientComm(ByVal client As Object)
        Try
            Dim tcpClient As TcpClient = CType(client, TcpClient)
            Dim clientStream As NetworkStream = tcpClient.GetStream
            Dim message() As Byte = New Byte((4096) - 1) {}
            Dim encoder As ASCIIEncoding = New ASCIIEncoding
            Dim bytesRead As Integer
            Dim fullMsg As String = ""
            'Dim clientIP As String = CType(tcpClient.Client.RemoteEndPoint, IPEndPoint).Address.ToString

            While True
                bytesRead = 0
                Try
                    'blocks until a client sends a message
                    bytesRead = clientStream.Read(message, 0, 4096)
                Catch ex As System.Exception
                    'a socket error has occured
                    Exit While
                End Try

                fullMsg += encoder.GetString(message, 0, bytesRead)

                If (bytesRead = 0) Or fullMsg.Contains(vbCrLf) Then
                    'the client has disconnected from the server
                    fullMsg = fullMsg.Replace(vbCrLf, "")
                    Exit While
                End If

            End While

            Console.WriteLine("Command Received: """ & fullMsg & """ (" & Now & ")")
            If logFile <> "" Then File.AppendAllText(logFile, vbCrLf & vbCrLf & "*** Start Command To Tandora Proxy On " & Date.Now.ToString() & " ***" & vbCrLf & fullMsg.Trim & vbCrLf & "*** End Command To Tandora Proxy ***")

            Dim sendStr As String = "", isSongSelected As Boolean = pianobarLast.Substring(0, 3) = "#  "

            If isTandoraActive Then

                If fullMsg.Contains("change station") Then

                    'Capture current song info so I know when pianobar has finished selecting new song
                    Dim curPianobarDur As String = pCurTime.Substring(pCurTime.IndexOf("/"))

                    'See if song selected, if so need to press 's' first to change station
                    If isSongSelected Then cmd = "s|,|"
                    cmd += pianobarStations.IndexOf(fullMsg.Substring(15)) & vbCrLf

                    Dim loopCount As Integer = 0

                    'Wait for pianobar to select song and start playing
                    Do Until curPianobarDur <> pCurTime.Substring(pCurTime.IndexOf("/")) Or loopCount > 300 Or pErrorTxt <> ""
                        Threading.Thread.Sleep(10)
                        loopCount += 1
                    Loop

                ElseIf isSongSelected AndAlso fullMsg.Contains("playpause") Then
                    cmd = "p"

                ElseIf isSongSelected AndAlso fullMsg.Contains("next") Then
                    Dim curPianobarDur As String = pCurTime.Substring(pCurTime.IndexOf("/"))
                    cmd = "n"
                    Do Until curPianobarDur <> pCurTime.Substring(pCurTime.IndexOf("/"))
                        Threading.Thread.Sleep(10)
                    Loop

                ElseIf isSongSelected AndAlso fullMsg.Contains("thumbsdown") Then
                    Dim curPianobarDur As String = pCurTime.Substring(pCurTime.IndexOf("/"))
                    cmd = "-"
                    Do Until curPianobarDur <> pCurTime.Substring(pCurTime.IndexOf("/"))
                        Threading.Thread.Sleep(100)
                    Loop

                ElseIf isSongSelected AndAlso fullMsg.Contains("thumpsup") Then
                    cmd = "+"
                End If

                If Trim(pStation) = "" Then pStation = "** No Station Selected **"
                If Trim(pSong) = "" Then pSong = "** No Song Selected **"

                sendStr = "IS PLAYING: " & pIsPlaying & vbCrLf &
                                        "STATION LIST: " & String.Join("|", pianobarStations.ToArray) & vbCrLf &
                                        "CURRENT STATION: " & pStation & vbCrLf &
                                        "CURRENT SONG: """ & pSong.Replace(ChrW(233), "e") & """ by """ & pArtist & """ on """ & pAlbum & """" & vbCrLf &
                                        "CURRENT TIME: " & pCurTime & vbCrLf &
                                        "ERROR TEXT: " & pErrorTxt & vbCrLf

            Else
                sendStr = "Waiting for TandoraProxy to start up..."
            End If

            If logFile <> "" Then File.AppendAllText(logFile, vbCrLf & vbCrLf & "*** Start Response From Tandora Proxy On " & Date.Now.ToString() & " ***" & vbCrLf & sendStr.Trim & vbCrLf & "*** End Response From Tandora Proxy ***")

            Dim sendBytes As Byte() = Encoding.ASCII.GetBytes(sendStr)
            clientStream.Write(sendBytes, 0, sendBytes.Length)

            tcpClient.Close()
        Catch e As Exception
            'Console.WriteLine(e.StackTrace)
            System.Diagnostics.EventLog.WriteEntry("TandoraProxy", e.Message & vbCrLf & vbCrLf & e.StackTrace, EventLogEntryType.Error)
        End Try
    End Sub

    Protected Overrides Sub OnStop()
        'EventLog.WriteEntry("TandoraProxy", "TandoraProxy stopped", EventLogEntryType.Information, 1, 1)
    End Sub

End Class
