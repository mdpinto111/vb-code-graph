Imports Shop.Billing

Partial Public Class Checkout
    Inherits System.Web.UI.Page

    Protected Sub Page_Load(sender As Object, e As EventArgs) Handles Me.Load
        If Not IsPostBack Then
            BindSummary()
        End If
    End Sub

    Protected Sub btnConfirm_Click(sender As Object, e As EventArgs) Handles btnConfirm.Click
        Dim service As New OrderService()
        Dim orderId = service.PlaceOrder(CurrentCustomerId(), CartLines())
        Response.Redirect("Confirmation.aspx?id=" & orderId)
    End Sub

    Private Sub BindSummary()
        Dim calculator As New PriceCalculator(0D)
        lblTotal.Text = FormatPrice(calculator.Total(CartLines()))
    End Sub

    Private Function CartLines() As List(Of OrderLine)
        Return CType(Session("cart"), List(Of OrderLine))
    End Function

    Private Function CurrentCustomerId() As Integer
        Return CInt(Session("customerId"))
    End Function
End Class
