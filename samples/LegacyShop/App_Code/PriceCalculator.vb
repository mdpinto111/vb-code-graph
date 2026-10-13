Namespace Shop.Billing
    Public Class PriceCalculator
        Private Const MAX_DISCOUNT As Decimal = 0.3D
        Private ReadOnly _discount As Decimal = 0D
        Private ReadOnly _log As New AuditLog()

        Public Sub New(discount As Decimal)
            _discount = ClampDiscount(discount)
        End Sub

        Public Function Subtotal(lines As List(Of OrderLine)) As Decimal
            Dim total As Decimal = 0D
            For Each line In lines
                total += line.UnitPrice * line.Quantity
            Next
            Return ApplyDiscount(total)
        End Function

        Public Function Shipping(subtotal As Decimal) As Decimal
            If subtotal >= Settings.FREE_SHIPPING_THRESHOLD Then
                Return 0D
            End If
            Return SHIPPING_FEE
        End Function

        Public Function Total(lines As List(Of OrderLine)) As Decimal
            Dim net = Subtotal(lines)
            Total = (net + Shipping(net)) * (1D + VAT_RATE)
            _log.Write("Total computed: " & FormatPrice(Total))
        End Function

        Private Function ApplyDiscount(amount As Decimal) As Decimal
            Return amount * (1D - _discount)
        End Function

        Private Function ClampDiscount(value As Decimal) As Decimal
            Return Math.Min(Math.Max(value, 0D), MAX_DISCOUNT)
        End Function
    End Class

    Public Class OrderLine
        Public Property Sku As String
        Public Property UnitPrice As Decimal
        Public Property Quantity As Integer
    End Class
End Namespace
