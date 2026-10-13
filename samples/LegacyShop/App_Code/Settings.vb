Public Module Settings
    Public Const VAT_RATE As Decimal = 0.2D
    Public Const FREE_SHIPPING_THRESHOLD As Decimal = 100D
    Public Const SHIPPING_FEE As Decimal = 6.9D
    Public Const CURRENCY As String = "EUR"

    Public Function FormatPrice(amount As Decimal) As String
        Return amount.ToString("0.00") & " " & CURRENCY
    End Function
End Module
