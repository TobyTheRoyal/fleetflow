import { lazy } from 'react'
import { Route, Routes, Link } from 'react-router-dom'
import { AppLayout } from '@/components/layout/AppLayout'
import { EmptyState } from '@/components/shared/Feedback'
import { Button } from '@/components/ui/button'
const DashboardPage = lazy(() =>
  import('@/features/dashboard/DashboardPage').then((module) => ({
    default: module.DashboardPage,
  })),
)
const ShipmentsPage = lazy(() =>
  import('@/features/shipments/ShipmentsPage').then((module) => ({
    default: module.ShipmentsPage,
  })),
)
const ShipmentForm = lazy(() =>
  import('@/features/shipments/ShipmentForm').then((module) => ({
    default: module.ShipmentForm,
  })),
)
const ShipmentEditPage = lazy(() =>
  import('@/features/shipments/ShipmentEditPage').then((module) => ({
    default: module.ShipmentEditPage,
  })),
)
const ShipmentDetailPage = lazy(() =>
  import('@/features/shipments/ShipmentDetailPage').then((module) => ({
    default: module.ShipmentDetailPage,
  })),
)
const CustomersPage = lazy(() =>
  import('@/features/customers/CustomersPage').then((module) => ({
    default: module.CustomersPage,
  })),
)
const DriversPage = lazy(() =>
  import('@/features/drivers/DriversPage').then((module) => ({
    default: module.DriversPage,
  })),
)
const VehiclesPage = lazy(() =>
  import('@/features/vehicles/VehiclesPage').then((module) => ({
    default: module.VehiclesPage,
  })),
)
export function App() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<DashboardPage />} />
        <Route path="shipments" element={<ShipmentsPage />} />
        <Route path="shipments/new" element={<ShipmentForm />} />
        <Route path="shipments/:id" element={<ShipmentDetailPage />} />
        <Route path="shipments/:id/edit" element={<ShipmentEditPage />} />
        <Route path="customers" element={<CustomersPage />} />
        <Route path="drivers" element={<DriversPage />} />
        <Route path="vehicles" element={<VehiclesPage />} />
        <Route
          path="*"
          element={
            <EmptyState
              title="Page not found"
              description="This workspace page does not exist. Return to your operations overview."
              action={
                <Button asChild>
                  <Link to="/">Go to overview</Link>
                </Button>
              }
            />
          }
        />
      </Route>
    </Routes>
  )
}
