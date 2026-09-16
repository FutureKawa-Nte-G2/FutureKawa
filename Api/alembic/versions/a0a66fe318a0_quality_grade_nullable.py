"""batches.quality_grade becomes nullable

Revision ID: a0a66fe318a0
Revises: 91f4b2832351
Create Date: 2026-09-05 10:12:33.481902

The initial schema made the grade mandatory. The ERP cannot honour that: Odoo
computes `quality_grade` from the product code and sends `null` for anything
that is not COFFEE-A/B/C, so a reception file for any other product would be
refused outright. A batch exists physically whether or not its grade is known,
and losing the traceability of the whole batch over a secondary attribute is
the worse trade.

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = 'a0a66fe318a0'
down_revision: Union[str, Sequence[str], None] = '91f4b2832351'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    """Upgrade schema."""
    op.alter_column(
        "batches",
        "quality_grade",
        existing_type=sa.String(length=8),
        nullable=True,
    )


def downgrade() -> None:
    """Downgrade schema.

    Fails loudly when batches without a grade already exist, which is the point:
    the alternative would be inventing a grade to satisfy the constraint, and a
    quality attribute is not something to make up. Clear the rows deliberately,
    then downgrade.
    """
    op.alter_column(
        "batches",
        "quality_grade",
        existing_type=sa.String(length=8),
        nullable=False,
    )
