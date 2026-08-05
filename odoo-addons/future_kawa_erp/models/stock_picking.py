# -*- coding: utf-8 -*-

import logging

from odoo import models, fields

_logger = logging.getLogger(__name__)


class StockPicking(models.Model):
    """
    Extension of the stock.picking model for FutureKawa.

    Adds a delivery reference field to link Odoo deliveries
    with the .NET backend tracking system.
    """

    _inherit = "stock.picking"

    delivery_reference = fields.Char(
        string="FutureKawa Delivery Reference",
        help="Delivery reference in the FutureKawa system",
        copy=False,
    )

    carrier_tracking_ref = fields.Char(
        string="Carrier Tracking Reference",
        help="Carrier tracking reference",
        copy=False,
    )
