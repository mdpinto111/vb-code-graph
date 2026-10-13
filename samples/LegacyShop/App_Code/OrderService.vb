Imports Shop.Billing

Public Class OrderService
    Private ReadOnly _log As New AuditLog()

    Public Function PlaceOrder(customerId As Integer, lines As List(Of OrderLine)) As Integer
        ValidateLines(lines)
        Dim calculator As New PriceCalculator(LoadDiscount(customerId))
        Dim amount = calculator.Total(lines)
        Dim orderId = SaveOrder(customerId, amount)
        _log.Write("Order " & orderId & " placed")
        Return orderId
    End Function

    Private Sub ValidateLines(lines As List(Of OrderLine))
        If lines Is Nothing OrElse lines.Count = 0 Then
            Throw New ArgumentException("An order needs at least one line.")
        End If
    End Sub

    Private Function LoadDiscount(customerId As Integer) As Decimal
        Return If(customerId Mod 2 = 0, 0.1D, 0D)
    End Function

    Private Function SaveOrder(customerId As Integer, amount As Decimal) As Integer
        Return New Random().Next(1000, 9999)
    End Function

    ' Kept from an older checkout flow; nothing calls it any more.
    Public Function LegacyTotal(amount As Decimal) As Decimal
        Return amount * (1D + VAT_RATE)
    End Function
End Class

Public Class AuditLog
    Public Sub Write(message As String)
        Console.WriteLine(Date.Now.ToString("s") & " " & message)
    End Sub
End Class
