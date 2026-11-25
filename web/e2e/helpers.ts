import { BrowserContext, expect, Locator, Page } from '@playwright/test';

export function voteControls(page: Page, topic: string): Locator {
  return page.locator('app-meeting-topic-votes').filter({
    has: page.getByRole('button', { name: `Проголосовать: ${topic}`, exact: true }),
  });
}

export async function advanceBeforeEveryoneIsReady(page: Page, action: string): Promise<void> {
  await page.getByRole('button', { name: action, exact: true }).click();
  await page.getByRole('dialog', { name: 'Не все участники готовы' })
    .getByRole('button', { name: 'Перейти дальше', exact: true }).click();
}

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
