export function DashboardPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold">HeroScript Dashboard</h1>
        <p className="text-muted-foreground mt-2">
          Welcome to the HeroScript Game Engine Management Dashboard
        </p>
      </div>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        <div className="rounded-lg border bg-card p-6">
          <h3 className="font-semibold text-lg">Actions</h3>
          <p className="text-sm text-muted-foreground mt-2">
            Manage combat actions and abilities
          </p>
        </div>

        <div className="rounded-lg border bg-card p-6">
          <h3 className="font-semibold text-lg">Entities</h3>
          <p className="text-sm text-muted-foreground mt-2">
            Create and edit heroes, enemies, and NPCs
          </p>
        </div>

        <div className="rounded-lg border bg-card p-6">
          <h3 className="font-semibold text-lg">Status Effects</h3>
          <p className="text-sm text-muted-foreground mt-2">
            Define buffs, debuffs, and conditions
          </p>
        </div>

        <div className="rounded-lg border bg-card p-6">
          <h3 className="font-semibold text-lg">Gambits</h3>
          <p className="text-sm text-muted-foreground mt-2">
            Configure AI behavior patterns
          </p>
        </div>
      </div>

      <div className="rounded-lg border bg-card p-6">
        <h2 className="text-xl font-semibold mb-4">Quick Start</h2>
        <div className="space-y-2 text-sm">
          <p>1. Define your Actions in the Actions page</p>
          <p>2. Create Entities (heroes and enemies)</p>
          <p>3. Set up Status Effects for your game</p>
          <p>4. Configure AI behavior with Gambits</p>
          <p>5. Test everything in the Combat Simulator</p>
        </div>
      </div>
    </div>
  );
}
