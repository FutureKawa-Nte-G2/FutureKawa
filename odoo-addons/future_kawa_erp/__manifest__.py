# -*- coding: utf-8 -*-

{
    "name": "FutureKawa ERP Integration",
    "version": "18.0.1.0.0",
    "summary": "Custom module for coffee order and delivery management",
    "description": """
FutureKawa ERP Integration
===========================

Custom Odoo module for coffee order and delivery management.

Features:
- Extension of the sale.order model with specific business fields (batch references, quality grade, country)
- Automatic webhook to the .NET backend on order confirmation, sending batch references as a list
- Integration status tracking with the .NET backend
- Extension of the stock.picking model for delivery tracking

Integration:
- Odoo → .NET Backend: HTTP POST webhook on order confirmation
- .NET Backend → Odoo: status update via JSON-RPC when the batch is shipped
    """,
    "author": "FutureKawa-Nte-G2",
    "website": "https://github.com/FutureKawa-Nte-G2",
    "category": "Sales/Inventory",
    "license": "LGPL-3",
    "depends": [
        "sale_management",
        "stock",
    ],
    "data": [
        "security/ir.model.access.csv",
        "data/ir_sequence.xml",
        "data/ir_config_parameter.xml",
        "views/sale_order_views.xml",
    ],
    "demo": [
        "data/demo_data.xml",
    ],
    "installable": True,
    "application": False,
    "auto_install": False,
}
