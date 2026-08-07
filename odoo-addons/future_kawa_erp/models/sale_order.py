# -*- coding: utf-8 -*-

from odoo.exceptions import UserError
from odoo import models, fields, api, _
import json
import logging
import requests
import urllib3

# Disable SSL warnings for the .NET backend self-signed certificate (dev)
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)


_logger = logging.getLogger(__name__)


class SaleOrder(models.Model):
    """
    Extension of the sale.order model for FutureKawa.

    Adds business fields specific to coffee management:
    - batch_count: number of coffee batches to generate
    - batch_ref: generated coffee batch reference(s), comma-separated (readonly)
    - quality_grade: quality grade (A, B, C) — informational on Odoo side
    - country: country of origin — informational on Odoo side
    - integration_status: sync status with the .NET backend

    Overrides the action_confirm() method to trigger a webhook
    to the .NET backend when an order is confirmed.
    """

    _inherit = "sale.order"

    # ── FutureKawa specific business fields ──

    batch_count = fields.Integer(
        string="Number of Batches",
        help="Number of coffee batches to generate for this order.",
        default=1,
        copy=False,
    )

    batch_ref = fields.Char(
        string="Generated Batch References",
        help="Coffee batch reference(s) generated for this order. "
             "Use commas to separate multiple references.",
        copy=False,
        readonly=True,
    )

    quality_grade = fields.Selection(
        selection=[
            ("a", "Grade A"),
            ("b", "Grade B"),
            ("c", "Grade C"),
        ],
        string="Quality Grade",
        help="Quality grade inferred from the selected coffee product",
        compute="_compute_quality_grade",
        store=True,
        readonly=True,
    )

    country = fields.Selection(
        selection=[
            ("BR", "Brésil"),
            ("EC", "Équateur"),
            ("CO", "Colombie"),
        ],
        string="Country",
        help="Country of origin of the coffee",
    )

    integration_status = fields.Selection(
        selection=[
            ("pending", "Pending"),
            ("synced", "Synced"),
            ("shipped", "Shipped"),
            ("error", "Error"),
        ],
        string="Integration Status",
        default="pending",
        help="Sync status with the .NET backend",
        copy=False,
        readonly=True,
    )

    integration_error_message = fields.Text(
        string="Integration Error Message",
        copy=False,
        readonly=True,
    )

    # ── Compute quality grade from product ──

    @api.depends("order_line", "order_line.product_id", "order_line.product_id.default_code")
    def _compute_quality_grade(self):
        """
        Infer the quality grade from the first FutureKawa coffee product
        listed on the order lines. This avoids asking the user to select
        the grade twice (once via the product, once via the FutureKawa tab).
        """
        for order in self:
            grade = False
            for line in order.order_line:
                code = (line.product_id.default_code or "").upper()
                if code == "COFFEE-A":
                    grade = "a"
                    break
                elif code == "COFFEE-B":
                    grade = "b"
                    break
                elif code == "COFFEE-C":
                    grade = "c"
                    break
            order.quality_grade = grade

    # ── Override of action_confirm ──

    def action_confirm(self):
        """
        Override of the standard action_confirm method.

        1. Calls the parent method (super) to execute Odoo's standard logic
           (order confirmation, picking generation, etc.)
        2. Triggers the webhook to the .NET backend to notify order creation.

        This approach follows Odoo's extension pattern: we override
        an existing method and add specific behavior without
        modifying Odoo's source code.
        """
        res = super().action_confirm()

        for order in self:
            order._notify_dotnet_backend()

        return res

    # ── Integration method: Odoo → .NET Backend ──

    def _notify_dotnet_backend(self):
        """
        Sends order data to the .NET backend via an HTTP POST webhook.

        The JSON payload contains (camelCase to match the .NET DTO):
        - orderId: the Odoo order ID
        - client: the client (partner) name
        - orderDate: the order date (ISO 8601)
        - batchReferences: list of coffee batch references (can be several)
        - lines: the order lines (product, quantity)

        The webhook URL and authentication token are retrieved from
        Odoo system parameters (ir.config_parameter):
        - future_kawa.webhook_url: .NET endpoint URL
        - future_kawa.webhook_token: shared token for authentication

        On failure, the integration status is set to 'error' and the error
        message is stored for diagnostics.
        """
        self.ensure_one()

        # Retrieve system parameters
        config_param = self.env["ir.config_parameter"].sudo()
        webhook_url = config_param.get_param("future_kawa.webhook_url")
        webhook_token = config_param.get_param("future_kawa.webhook_token")

        if not webhook_url:
            _logger.warning(
                "FutureKawa: webhook URL not configured "
                "(system parameter 'future_kawa.webhook_url')"
            )
            return

        # Generate batch references automatically if not already set
        batch_refs = []
        if self.batch_ref:
            batch_refs = [
                ref.strip()
                for ref in self.batch_ref.split(",")
                if ref.strip()
            ]
        elif self.batch_count and self.batch_count > 0:
            sequence = self.env["ir.sequence"].sudo()
            batch_refs = [
                sequence.next_by_code("future_kawa.batch.reference")
                for _ in range(self.batch_count)
            ]
            self.write({"batch_ref": ", ".join(batch_refs)})

        # Build JSON payload (camelCase to match .NET DTO)
        payload = {
            "orderId": self.id,
            "client": self.partner_id.name,
            "orderDate": self.date_order.isoformat() if self.date_order else None,
            "country": self.country or None,
            "qualityGrade": self.quality_grade or None,
            "batchReferences": batch_refs,
            "lines": [
                {
                    "product": line.product_id.name,
                    "quantity": line.product_uom_qty,
                }
                for line in self.order_line
            ],
        }

        # HTTP headers with authentication token
        headers = {
            "Content-Type": "application/json",
        }
        if webhook_token:
            headers["X-Webhook-Token"] = webhook_token

        # ── Detailed sync trace ──
        _logger.info(
            "FutureKawa: ──────────────────────────────────────────────"
        )
        _logger.info(
            "FutureKawa: [SYNC] Starting order sync %s (ID=%s)",
            self.name,
            self.id,
        )
        _logger.info(
            "FutureKawa: [SYNC] URL webhook: %s",
            webhook_url,
        )
        _logger.info(
            "FutureKawa: [SYNC] Token: %s",
            (webhook_token[:8] + "..." + webhook_token[-4:]
             ) if webhook_token else "NOT DEFINED",
        )
        _logger.info(
            "FutureKawa: [SYNC] Client: %s",
            self.partner_id.name,
        )
        _logger.info(
            "FutureKawa: [SYNC] Order date: %s",
            self.date_order.isoformat() if self.date_order else "N/A",
        )
        _logger.info(
            "FutureKawa: [SYNC] Lines: %d",
            len(self.order_line),
        )
        _logger.info(
            "FutureKawa: [SYNC] Payload JSON: %s",
            json.dumps(payload, ensure_ascii=False),
        )

        try:
            response = requests.post(
                webhook_url,
                json=payload,
                headers=headers,
                timeout=10,
                # Self-signed certificate in dev (.NET backend Kestrel)
                verify=False,
            )

            _logger.info(
                "FutureKawa: [SYNC] .NET backend response: HTTP %d",
                response.status_code,
            )
            _logger.info(
                "FutureKawa: [SYNC] Response body: %s",
                response.text[:500],
            )

            if response.status_code == 200:
                self.write(
                    {
                        "integration_status": "synced",
                        "integration_error_message": False,
                    }
                )
                _logger.info(
                    "FutureKawa: [SYNC] Order %s synced successfully",
                    self.name,
                )
            else:
                error_msg = (
                    f"HTTP {response.status_code}: {response.text[:500]}"
                )
                self.write(
                    {
                        "integration_status": "error",
                        "integration_error_message": error_msg,
                    }
                )
                _logger.error(
                    "FutureKawa: [SYNC] Webhook failed for %s - %s",
                    self.name,
                    error_msg,
                )

        except requests.exceptions.RequestException as exc:
            error_msg = f"Network error: {str(exc)}"
            self.write(
                {
                    "integration_status": "error",
                    "integration_error_message": error_msg,
                }
            )
            _logger.error(
                "FutureKawa: [SYNC] Network error for %s - %s",
                self.name,
                error_msg,
            )

        _logger.info(
            "FutureKawa: ──────────────────────────────────────────────"
        )

    # ── Method called by the .NET backend (via JSON-RPC) ──

    def action_mark_shipped(self):
        """
        Marks the order as shipped.

        This method is called by the .NET backend via Odoo's JSON-RPC API
        when the batch associated with the order has been shipped.

        It updates the integration status to 'shipped' and records
        the shipping date.

        Note: This method is exposed via Odoo's web service API
        (execute_kw) and is accessible with Odoo credentials.
        """
        for order in self:
            order.write(
                {
                    "integration_status": "shipped",
                }
            )
            _logger.info(
                "FutureKawa: order %s marked as shipped by the .NET backend",
                order.name,
            )

    # ── Server action: retry webhook ──

    def action_retry_webhook(self):
        """
        Manual action to retry the webhook to the .NET backend.

        Useful when the .NET backend was unavailable during order
        confirmation and the integration status is 'error'.
        """
        for order in self:
            if order.integration_status == "error":
                order.write({"integration_status": "pending"})
                order._notify_dotnet_backend()
