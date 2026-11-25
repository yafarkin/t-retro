import { expect, Page, test } from '@playwright/test';
import { advanceBeforeEveryoneIsReady, closeContextGracefully } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

async function addCard(page: Page, columnName: string, text: string): Promise<void> {
  const column = page.getByRole('region', { name: columnName, exact: true });
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await column.getByLabel('Новая карточка').fill(text);
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await expect(column.getByText(text, { exact: true })).toBeVisible();
}

test('M4 manual groups, live moves, stub preview and drag and drop work in two contexts', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();

  await login(first, origin, 'm4-first', 'Первый M4');
  await login(second, origin, 'm4-second', 'Второй M4');
  await first.goto(`${origin}/meetings/new`);
  await first.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await first.getByLabel('Название встречи').fill('Ретро M4 группы');
  await first.getByRole('button', { name: 'Создать ретро', exact: true }).click();
  await first.getByRole('button', { name: 'Пригласить', exact: true }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await first.getByRole('button', { name: 'Закрыть приглашение' }).click();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Сбор карточек');

  await addCard(first, 'Начать', 'Первая A');
  await addCard(second, 'Перестать', 'Вторая B');
  await addCard(first, 'Начать', 'Третья C');
  await advanceBeforeEveryoneIsReady(first, 'Перейти к группировке');
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Группировка');
  await expect(first.getByRole('heading', { name: 'Доска', exact: true })).toHaveCount(0);
  await expect(first.locator('.phase-actions').getByRole('button')).toHaveText([
    'Предложить группировку', 'Открыть голосование',
  ]);

  const secondCard = second.locator('.card').filter({ has: second.getByText('Вторая B', { exact: true }) });
  const firstCard = second.locator('.card').filter({ has: second.getByText('Первая A', { exact: true }) });
  const originalColor = await secondCard.evaluate((card) => getComputedStyle(card).borderInlineStartColor);
  await expect(second.getByLabel('Назначение переноса')).toHaveCount(0);
  await expect(second.getByRole('button', { name: 'Переместить', exact: true })).toHaveCount(0);
  const sourceColumn = second.getByRole('region', { name: 'Начать', exact: true });
  const targetColumn = second.getByRole('region', { name: 'Перестать', exact: true });
  const dataTransfer = await second.evaluateHandle(() => new DataTransfer());
  await secondCard.dispatchEvent('dragstart', { dataTransfer });
  await expect(targetColumn.locator('.column-drop-space')).not.toHaveClass(/\bavailable\b/);
  await expect(sourceColumn.locator('.column-drop-space')).toHaveClass(/\bavailable\b/);
  await targetColumn.locator('.column-drop-space').dispatchEvent('dragover', { dataTransfer });
  await expect(targetColumn).not.toHaveClass(/\bdrag-target\b/);
  await secondCard.dispatchEvent('dragend', { dataTransfer });
  await dataTransfer.dispose();
  await secondCard.dragTo(firstCard);
  const firstGroup = first.locator('.card-group').filter({ hasText: 'Первая A' });
  await expect(firstGroup).toBeVisible();
  await expect(firstGroup.locator('.card')).toHaveCount(1);
  await expect(firstGroup.locator('.group-peek span')).toHaveCount(1);
  await firstGroup.getByRole('button', { name: 'Развернуть группу' }).click();
  await expect(firstGroup.locator('.card')).toHaveCount(2);
  await expect(firstGroup.locator('.card').filter({ hasText: 'Вторая B' })).toHaveCSS('border-inline-start-color', originalColor);
  await firstGroup.getByRole('button', { name: 'Свернуть группу' }).click();
  await expect(firstGroup.locator('.card')).toHaveCount(1);

  await firstGroup.getByRole('button', { name: 'Переименовать группу' }).click();
  await firstGroup.getByLabel('Название группы').fill('Общая тема');
  await firstGroup.getByRole('button', { name: 'Сохранить название' }).click();
  const secondGroup = second.locator('.card-group').filter({ hasText: 'Общая тема' });
  await expect(secondGroup).toBeVisible();
  await secondGroup.getByRole('button', { name: 'Развернуть группу' }).click();
  const extractedCard = secondGroup.locator('.card').filter({ has: second.getByText('Первая A', { exact: true }) });
  await extractedCard.dragTo(sourceColumn.locator('.column-drop-space'));
  await expect(second.locator('.card-group')).toHaveCount(0);
  await expect(first.locator('.card-group')).toHaveCount(0);

  await first.getByRole('button', { name: 'Предложить группировку' }).click();
  await expect(first.getByRole('heading', { name: 'Предпросмотр тестовой заглушки' })).toBeVisible();
  await expect(first.locator('.proposal')).toContainText('Тестовая группа 1');
  await first.getByRole('button', { name: 'Применить предложение' }).click();
  const appliedOnSecond = second.locator('.card-group').filter({ hasText: 'Тестовая группа 1' });
  await expect(appliedOnSecond).toBeVisible();

  await appliedOnSecond.locator('.group-heading').dragTo(targetColumn.locator('.column-drop-space'));
  const movedGroup = targetColumn.locator('.card-group').filter({ hasText: 'Тестовая группа 1' });
  await expect(movedGroup).toBeVisible();
  const freeCard = sourceColumn.locator('.card').filter({ has: second.getByText('Третья C', { exact: true }) });
  await freeCard.dragTo(movedGroup.locator('.group-heading'));
  await movedGroup.getByRole('button', { name: 'Развернуть группу' }).click();
  await expect(movedGroup.locator('.card')).toHaveCount(3);
  await expect(movedGroup).toContainText('Третья C');

  await Promise.all([firstContext, secondContext].map(closeContextGracefully));
});
