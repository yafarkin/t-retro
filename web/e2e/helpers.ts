import { BrowserContext, expect } from '@playwright/test';

export async function closeContextGracefully(context: BrowserContext): Promise<void> {
  await Promise.all(context.pages().map(async (page) => {
    if (!page.isClosed()) {
      await page.evaluate(() => window.dispatchEvent(new Event('pagehide')));
      const connection = page.locator('.connection');
      if (await connection.count()) {
        await expect(connection).toContainText('Нет соединения', { timeout: 5_000 });
      }
      await page.close({ runBeforeUnload: true });
    }
  }));
  await context.close();
}
