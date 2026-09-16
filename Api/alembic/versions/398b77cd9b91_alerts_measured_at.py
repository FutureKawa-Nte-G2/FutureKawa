"""alerts.measured_at

Revision ID: 398b77cd9b91
Revises: 3a6297571e74
Create Date: 2026-09-14 10:00:00.000000

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '398b77cd9b91'
down_revision: Union[str, Sequence[str], None] = '3a6297571e74'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    """Upgrade schema."""
    op.add_column('alerts', sa.Column('measured_at', sa.DateTime(timezone=True), nullable=True))


def downgrade() -> None:
    """Downgrade schema."""
    op.drop_column('alerts', 'measured_at')
