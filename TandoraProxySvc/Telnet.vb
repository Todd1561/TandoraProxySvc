Imports System.Text
Imports System.Net.Sockets

Namespace MinimalisticTelnet
    Enum Verbs
        WILL = 251
        WONT = 252
        [DO] = 253
        DONT = 254
        IAC = 255
    End Enum

    Enum Options
        SGA = 3
    End Enum

    Class TelnetConnection
        Private tcpSocket As TcpClient
        Private TimeOutMs As Integer = 100

        Public Sub New(ByVal Hostname As String, ByVal Port As Integer)
            tcpSocket = New TcpClient(Hostname, Port)
        End Sub

        Public Function Login(ByVal Username As String, ByVal Password As String, ByVal LoginTimeOutMs As Integer) As String
            Dim oldTimeOutMs As Integer = TimeOutMs
            TimeOutMs = LoginTimeOutMs
            Dim s As String = Read()
            If Not s.TrimEnd().EndsWith(":") Then Throw New Exception("Failed to connect : no login prompt")
            WriteLine(Username)
            s += Read()
            If Not s.TrimEnd().EndsWith(":") Then Throw New Exception("Failed to connect : no password prompt")
            WriteLine(Password)
            s += Read()
            TimeOutMs = oldTimeOutMs
            Return s
        End Function

        Public Sub WriteLine(ByVal cmd As String)
            Write(cmd & vbCrLf)
        End Sub

        Public Sub Write(ByVal cmd As String)
            If Not tcpSocket.Connected Then Return
            Dim buf As Byte() = System.Text.ASCIIEncoding.ASCII.GetBytes(cmd.Replace(vbNullChar & "xFF", vbNullChar & "xFF" & vbNullChar & "xFF"))
            tcpSocket.GetStream().Write(buf, 0, buf.Length)
        End Sub

        Public Function Read() As String
            If Not tcpSocket.Connected Then Return Nothing
            Dim sb As StringBuilder = New StringBuilder()

            Do
                ParseTelnet(sb)
                System.Threading.Thread.Sleep(TimeOutMs)
            Loop While tcpSocket.Available > 0

            Return sb.ToString()
        End Function

        Public ReadOnly Property IsConnected As Boolean
            Get
                Return tcpSocket.Connected
            End Get
        End Property

        Private Sub ParseTelnet(ByVal sb As StringBuilder)
            While tcpSocket.Available > 0
                Dim input As Integer = tcpSocket.GetStream().ReadByte()

                Select Case input
                    Case -1
                    Case CInt(Verbs.IAC)
                        Dim inputverb As Integer = tcpSocket.GetStream().ReadByte()
                        If inputverb = -1 Then Exit Select

                        Select Case inputverb
                            Case CInt(Verbs.IAC)
                                sb.Append(inputverb)
                            Case CInt(Verbs.[DO]), CInt(Verbs.DONT), CInt(Verbs.WILL), CInt(Verbs.WONT)
                                Dim inputoption As Integer = tcpSocket.GetStream().ReadByte()
                                If inputoption = -1 Then Exit Select
                                tcpSocket.GetStream().WriteByte(CByte(Verbs.IAC))

                                If inputoption = CInt(Options.SGA) Then
                                    tcpSocket.GetStream().WriteByte(If(inputverb = CInt(Verbs.[DO]), CByte(Verbs.WILL), CByte(Verbs.[DO])))
                                Else
                                    tcpSocket.GetStream().WriteByte(If(inputverb = CInt(Verbs.[DO]), CByte(Verbs.WONT), CByte(Verbs.DONT)))
                                End If

                                tcpSocket.GetStream().WriteByte(CByte(inputoption))
                            Case Else
                        End Select

                    Case Else
                        sb.Append(ChrW(input))
                End Select
            End While
        End Sub
    End Class
End Namespace
